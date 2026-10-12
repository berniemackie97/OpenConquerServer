using System.Text.Json;
using OpenConquer.Domain.Items;

namespace OpenConquer.GameData.Tool.Items;

internal static class ItemTypeCatalogFileWriter
{
    public const int FormatVersion = 2;

    public static void Write(string destinationPath, IReadOnlyList<ItemTypeDefinition> definitions, string sourceSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(definitions);
        ValidateSourceSha256(sourceSha256);

        if (definitions.Count == 0)
        {
            throw new ArgumentException("An item-type catalog cannot be empty.", nameof(definitions));
        }

        ItemTypeDefinition[] ordered = definitions.ToArray();
        HashSet<uint> observedItemTypeIds = [];

        foreach (ItemTypeDefinition? definition in ordered)
        {
            if (definition is null)
            {
                throw new ArgumentException("An item-type catalog cannot contain a null definition.", nameof(definitions));
            }

            if (!observedItemTypeIds.Add(definition.ItemTypeId))
            {
                throw new ArgumentException($"Item-type catalog contains duplicate item type {definition.ItemTypeId}.", nameof(definitions));
            }
        }

        Array.Sort(ordered, static (left, right) => left.ItemTypeId.CompareTo(right.ItemTypeId));

        string fullPath = Path.GetFullPath(destinationPath);
        string directoryPath = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Item-type catalog destination must have a parent directory.", nameof(destinationPath));

        Directory.CreateDirectory(directoryPath);

        string temporaryPath = Path.Combine(directoryPath, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (FileStream stream = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                bufferSize: 81920, FileOptions.SequentialScan))
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("formatVersion", FormatVersion);
                writer.WriteString("sourceSha256", sourceSha256);
                writer.WritePropertyName("itemTypes");
                writer.WriteStartArray();

                foreach (ItemTypeDefinition definition in ordered)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("itemTypeId", definition.ItemTypeId);
                    writer.WriteString("name", definition.Name);
                    writer.WriteNumber("requiredLevel", definition.RequiredLevel);
                    writer.WriteNumber("speedPercentOffset", definition.SpeedPercentOffset);
                    writer.WriteNumber("life", definition.Life);
                    writer.WriteNumber("mana", definition.Mana);
                    writer.WriteNumber("initialDurability", definition.InitialDurability);
                    writer.WriteNumber("maximumDurability", definition.MaximumDurability);
                    writer.WriteNumber("staticLifetimeMinutes", definition.StaticLifetimeMinutes);
                    writer.WriteNumber("stackCapacity", definition.StackCapacity);
                    writer.WriteNumber("requiredProfessionCode", definition.RequiredProfessionCode);
                    writer.WriteNumber("monopolyFlags", definition.MonopolyFlags);
                    writer.WriteNumber("silverPrice", definition.SilverPrice);
                    writer.WriteNumber("attackRange", definition.AttackRange);
                    writer.WriteNumber("attackSpeed", definition.AttackSpeed);
                    writer.WriteNumber("conquerPointsPrice", definition.ConquerPointsPrice);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateSourceSha256(string sourceSha256)
    {
        if (sourceSha256 is not { Length: 64 } || sourceSha256.Any(c => !((c is >= '0' and <= '9') || (c is >= 'a' and <= 'f'))))
        {
            throw new ArgumentException("An item-type release requires a lowercase SHA-256 source fingerprint.", nameof(sourceSha256));
        }
    }
}
