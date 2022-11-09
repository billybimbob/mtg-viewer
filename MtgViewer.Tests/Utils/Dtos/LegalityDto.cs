using System.Text.Json.Serialization;

using MtgApiManager.Lib.Model;

namespace MtgViewer.Tests.Utils.Dtos;

internal class LegalityDto : ILegality
{
    [JsonPropertyName("format")]
    public required string Format { get; set; }

    [JsonPropertyName("legality")]
    public required string LegalityName { get; set; }
}
