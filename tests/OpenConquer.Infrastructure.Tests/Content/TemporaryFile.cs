namespace OpenConquer.Infrastructure.Tests.Content;

internal sealed class TemporaryFile : IDisposable
{
    public TemporaryFile(string content)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"openconquer-content-{Guid.NewGuid():N}.json");
        File.WriteAllText(Path, content);
    }

    public string Path { get; }

    public void Dispose() => File.Delete(Path);
}
