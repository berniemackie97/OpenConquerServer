using OpenConquer.Application.World;
using SharpCompress.Archives;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace OpenConquer.GameData.Tool.Maps;

internal sealed class NativeMapSourceFiles
{
    private readonly string _rootPath;
    private readonly MapTerrainLoadLimits _limits;

    public NativeMapSourceFiles(string rootPath, MapTerrainLoadLimits limits)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(limits);

        _rootPath = Path.GetFullPath(rootPath);
        _limits = limits;

        DirectoryInfo directory = new(_rootPath);

        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"Client asset root '{_rootPath}' does not exist.");
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 || directory.LinkTarget is not null)
        {
            throw new IOException($"Client asset root '{_rootPath}' cannot be a symbolic link.");
        }
    }

    public string ResolveFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (relativePath.Length > 4096)
        {
            throw new InvalidDataException("Client asset path exceeds the supported length.");
        }

        string normalized = relativePath.Replace('\\', '/');

        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Contains('\0')
            || normalized.Any(char.IsControl))
        {
            throw new InvalidDataException($"Client asset path '{relativePath}' is invalid.");
        }

        string[] segments = normalized.Split('/');

        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException($"Client asset path '{relativePath}' contains an invalid segment.");
        }

        string current = _rootPath;

        for (int index = 0; index < segments.Length; index++)
        {
            string segment = segments[index];
            string? match = null;

            foreach (string candidate in Directory.EnumerateFileSystemEntries(current))
            {
                if (!string.Equals(Path.GetFileName(candidate), segment, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (match is not null)
                {
                    throw new InvalidDataException(
                        $"Client asset path '{relativePath}' has ambiguous case-insensitive matches.");
                }

                match = candidate;
            }

            if (match is null)
            {
                throw new FileNotFoundException(
                    $"Client asset path '{relativePath}' could not be resolved under '{_rootPath}'.");
            }

            FileAttributes attributes = File.GetAttributes(match);

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException($"Client asset path '{relativePath}' traverses a symbolic link.");
            }

            bool isDirectory = (attributes & FileAttributes.Directory) != 0;

            if (index < segments.Length - 1 && !isDirectory)
            {
                throw new InvalidDataException($"Client asset path '{relativePath}' has a non-directory component.");
            }

            if (index == segments.Length - 1 && isDirectory)
            {
                throw new InvalidDataException($"Client asset path '{relativePath}' does not identify a regular file.");
            }

            current = match;
        }

        return current;
    }

    public T ReadMap<T>(string relativePath, Func<Stream, T> parse,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(parse);
        cancellationToken.ThrowIfCancellationRequested();

        string path = ResolveFile(relativePath);
        FileInfo file = new(path);

        if (file.Length is <= 0 || file.Length > _limits.MaximumContainerBytes)
        {
            throw new InvalidDataException(
                $"Map container '{relativePath}' is empty or exceeds the configured source limit.");
        }

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.SequentialScan);

        string extension = Path.GetExtension(path);

        if (string.Equals(extension, ".DMap", StringComparison.OrdinalIgnoreCase))
        {
            using BoundedMapStream payload = new(stream, _limits.MaximumContainerBytes);
            return parse(payload);
        }

        if (!string.Equals(extension, ".7z", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Map container '{relativePath}' has an unsupported extension.");
        }

        try
        {
            if (!SevenZipArchive.IsSevenZipFile(stream))
            {
                throw new InvalidDataException($"Map container '{relativePath}' has an invalid 7-Zip signature.");
            }

            stream.Position = 0;

            using IArchive archive = SevenZipArchive.OpenArchive(stream, new ReaderOptions { LeaveStreamOpen = true });
            IArchiveEntry? payloadEntry = null;

            foreach (IArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                {
                    continue;
                }

                if (payloadEntry is not null)
                {
                    throw new InvalidDataException($"Map container '{relativePath}' contains multiple file payloads.");
                }

                payloadEntry = entry;
            }

            if (payloadEntry is null || payloadEntry.Size is <= 0
                || payloadEntry.Size > _limits.MaximumContainerBytes)
            {
                throw new InvalidDataException($"Map container '{relativePath}' has no valid bounded payload.");
            }

            string entryName = payloadEntry.Key
                ?? throw new InvalidDataException($"Map container '{relativePath}' has an unnamed payload.");

            string normalized = entryName.Replace('\\', '/');

            if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Contains('\0')
                || normalized.Split('/').Any(segment => segment.Length == 0 || segment is "." or ".."))
            {
                throw new InvalidDataException($"Map container '{relativePath}' has an invalid entry path.");
            }

            string payloadName = Path.GetFileName(normalized);

            if (!string.Equals(Path.GetExtension(payloadName), ".DMap", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileNameWithoutExtension(payloadName),
                    Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Map container '{relativePath}' has an unexpected payload identity.");
            }

            using Stream entryStream = payloadEntry.OpenEntryStream();
            using BoundedMapStream payload = new(entryStream, _limits.MaximumContainerBytes);

            return parse(payload);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArchiveException or IOException
            or NotSupportedException or InvalidOperationException or ArgumentException or OverflowException)
        {
            throw new InvalidDataException($"Map container '{relativePath}' could not be decoded.", exception);
        }
    }

    private sealed class BoundedMapStream : Stream
    {
        private readonly Stream _source;
        private readonly long _maximumBytes;
        private long _consumed;

        public BoundedMapStream(Stream source, long maximumBytes)
        {
            _source = source;
            _maximumBytes = maximumBytes;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _consumed;
            set => throw new NotSupportedException();
        }

        public override int Read(Span<byte> buffer)
        {
            if (buffer.IsEmpty)
            {
                return 0;
            }

            long remaining = _maximumBytes - _consumed;

            if (remaining <= 0)
            {
                throw new InvalidDataException(
                    $"Decoded map content exceeds the configured limit of {_maximumBytes} bytes.");
            }

            int count = _source.Read(buffer[..(int)Math.Min(buffer.Length, remaining)]);
            _consumed += count;

            return count;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        public override int ReadByte()
        {
            Span<byte> buffer = stackalloc byte[1];
            return Read(buffer) == 0 ? -1 : buffer[0];
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
