using System.Text.Json.Serialization;

namespace OpenConquer.Infrastructure.Content.Items;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class ItemTypeCatalogFileDocument
{
    [JsonPropertyName("formatVersion")]
    public required int FormatVersion
    {
        get; init;
    }

    [JsonPropertyName("itemTypes")]
    public required ItemTypeCatalogFileEntry[] ItemTypes
    {
        get; init;
    }
}
