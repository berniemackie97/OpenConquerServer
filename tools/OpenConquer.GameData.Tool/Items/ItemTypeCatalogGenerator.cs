using System.Globalization;
using OpenConquer.Domain.Items;
using OpenConquer.GameData.Tool.Formats.Items;

namespace OpenConquer.GameData.Tool.Items;

internal static class ItemTypeCatalogGenerator
{
    public static ItemTypeDefinition[] Generate(ItemTypeDatTable source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.RecordCount == 0)
        {
            throw new InvalidDataException("Item-type source contains no definitions.");
        }

        ItemTypeDefinition[] definitions = new ItemTypeDefinition[source.RecordCount];
        int index = 0;

        foreach (ItemTypeDatRecord record in source.Records.OrderBy(record => record.ItemTypeId))
        {
            if (record.StaticLifetimeMinutes < 0)
            {
                throw new InvalidDataException($"Item type {record.ItemTypeId} has negative static lifetime {record.StaticLifetimeMinutes}.");
            }

            if ((uint)record.StaticLifetimeMinutes > ItemTypeDefinition.MaximumStaticLifetimeMinutes)
            {
                throw new InvalidDataException($"Item type {record.ItemTypeId} has static lifetime {record.StaticLifetimeMinutes} minutes, exceeding the supported maximum of {ItemTypeDefinition.MaximumStaticLifetimeMinutes} minutes.");
            }

            if (record.StackCapacity < 0 || record.StackCapacity > ushort.MaxValue)
            {
                throw new InvalidDataException($"Item type {record.ItemTypeId} has stack capacity {record.StackCapacity}, outside the supported unsigned 16-bit range.");
            }

            definitions[index++] = new ItemTypeDefinition(record.ItemTypeId, NormalizeName(record.Name),
                record.RequiredLevel, record.SpeedPercentOffset, record.Life, record.Mana,
                record.InitialDurability, record.MaximumDurability, checked((uint)record.StaticLifetimeMinutes),
                checked((ushort)record.StackCapacity),
                ParseInt32(record, 2, "required profession code"),
                ParseUInt32(record, 10, "monopoly flags"),
                ParseUInt32(record, 12, "silver price"),
                ParseInt32(record, 32, "attack range"),
                ParseInt32(record, 33, "attack speed"),
                ParseUInt32(record, 37, "Conquer Points price"));
        }

        return definitions;
    }

    private static int ParseInt32(ItemTypeDatRecord record, int fieldIndex, string name)
    {
        string value = record.FieldTexts[fieldIndex];

        if (!int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int parsed))
        {
            throw new InvalidDataException($"Item type {record.ItemTypeId} has invalid {name} '{value}' at native field {fieldIndex}.");
        }

        return parsed;
    }

    private static uint ParseUInt32(ItemTypeDatRecord record, int fieldIndex, string name)
    {
        string value = record.FieldTexts[fieldIndex];

        if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed))
        {
            throw new InvalidDataException($"Item type {record.ItemTypeId} has invalid {name} '{value}' at native field {fieldIndex}.");
        }

        return parsed;
    }

    private static string NormalizeName(string name)
    {
        return name.Replace('~', ' ');
    }
}
