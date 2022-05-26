using System.Text.Json.Serialization;

using MtgApiManager.Lib.Model;

namespace MtgViewer.Tests.Utils.Dtos;

internal class ForeignNameDto : IForeignName
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("multiverseid")]
    public int? MultiverseId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
