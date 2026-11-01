using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using OpenConquer.GameData.Tool.Formats.Items;

namespace OpenConquer.GameData.Tool.Items;

internal static class ItemTypeSourceFileWriter
{
    public const int FormatVersion = 1;

    public static void Write(string destinationPath, IReadOnlyCollection<ItemTypeDatRecord> records, ReadOnlySpan<byte> sourcePayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        if (sourcePayload.IsEmpty)
        {
            throw new ArgumentException("Item-type source bytes are required to record the source fingerprint.", nameof(sourcePayload));
        }

        if (records.Count == 0)
        {
            throw new ArgumentException("Item-type source catalog cannot be empty.", nameof(records));
        }

        ItemTypeDatRecord[] ordered = records.OrderBy(record => record.ItemTypeId).ToArray();
        uint previousId = 0;

        foreach (ItemTypeDatRecord record in ordered)
        {
            if (record.ItemTypeId == 0 || record.ItemTypeId <= previousId
                || record.FieldCount != ItemTypeDatRecord.RecordFieldCount
                || !uint.TryParse(record.FieldTexts[ItemTypeDatRecord.ItemTypeIdFieldIndex],
                    NumberStyles.None, CultureInfo.InvariantCulture, out uint encodedId)
                || encodedId != record.ItemTypeId)
            {
                throw new InvalidDataException("Item-type records must have unique, positive, consistent identities and exactly 59 fields.");
            }

            previousId = record.ItemTypeId;
        }

        string fullPath = Path.GetFullPath(destinationPath);
        string parent = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("An item-type source catalog needs a parent directory.", nameof(destinationPath));

        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, FileOptions.SequentialScan))
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("formatVersion", FormatVersion);
                writer.WriteString("sourceFormat", "itemtype.dat-cp936-59-fields");
                writer.WriteString("sourceSha256", Convert.ToHexStringLower(SHA256.HashData(sourcePayload)));
                writer.WriteNumber("fieldCount", ItemTypeDatRecord.RecordFieldCount);
                writer.WritePropertyName("itemTypes");
                writer.WriteStartArray();

                foreach (ItemTypeDatRecord record in ordered)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("itemTypeId", record.ItemTypeId);
                    writer.WritePropertyName("fields");
                    writer.WriteStartArray();

                    foreach (string field in record.FieldTexts)
                    {
                        writer.WriteStringValue(field);
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(staging, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(staging))
            {
                File.Delete(staging);
            }
        }
    }
}
