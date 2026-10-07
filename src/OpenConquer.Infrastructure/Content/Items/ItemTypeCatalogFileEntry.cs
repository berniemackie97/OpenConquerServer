using System.Text.Json.Serialization;

namespace OpenConquer.Infrastructure.Content.Items;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class ItemTypeCatalogFileEntry
{
    [JsonPropertyName("itemTypeId")]
    public required uint ItemTypeId
    {
        get; init;
    }

    [JsonPropertyName("name")]
    public required string Name
    {
        get; init;
    }

    [JsonPropertyName("requiredLevel")]
    public required byte RequiredLevel
    {
        get; init;
    }

    [JsonPropertyName("speedPercentOffset")]
    public required short SpeedPercentOffset
    {
        get; init;
    }

    [JsonPropertyName("life")]
    public required short Life
    {
        get; init;
    }

    [JsonPropertyName("mana")]
    public required short Mana
    {
        get; init;
    }

    [JsonPropertyName("initialDurability")]
    public required ushort InitialDurability
    {
        get; init;
    }

    [JsonPropertyName("maximumDurability")]
    public required ushort MaximumDurability
    {
        get; init;
    }

    [JsonPropertyName("staticLifetimeMinutes")]
    public required uint StaticLifetimeMinutes
    {
        get; init;
    }

    [JsonPropertyName("stackCapacity")]
    public required ushort StackCapacity
    {
        get; init;
    }
}
