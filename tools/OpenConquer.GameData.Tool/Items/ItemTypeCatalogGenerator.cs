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

            definitions[index++] = new ItemTypeDefinition(record.ItemTypeId, NormalizeName(record.Name), record.RequiredLevel, record.SpeedPercentOffset, record.Life, record.Mana, record.InitialDurability, record.MaximumDurability, checked((uint)record.StaticLifetimeMinutes), checked((ushort)record.StackCapacity));
        }

        return definitions;
    }

    private static string NormalizeName(string name)
    {
        return name.Replace('~', ' ');
    }
}
