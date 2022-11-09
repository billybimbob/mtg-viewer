using System.Text.Json.Serialization;

using MtgApiManager.Lib.Model;

namespace MtgViewer.Tests.Utils.Dtos;

internal class ForeignNameDto : IForeignName
{
    [JsonPropertyName("language")]
    public required string Language { get; set; }

    [JsonPropertyName("multiverseid")]
    public required int? MultiverseId { get; set; }

    [JsonPropertyName("name")]
    public required string Name { get; set; }
}
