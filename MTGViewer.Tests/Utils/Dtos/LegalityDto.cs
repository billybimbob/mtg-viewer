using System.Text.Json.Serialization;
using MtgApiManager.Lib.Model;

namespace MTGViewer.Tests.Utils.Dto;

internal class LegalityDto : ILegality
{
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;

    [JsonPropertyName("legality")]
    public string LegalityName { get; set; } = string.Empty;
}