using System.Text.Json.Serialization;

using MtgApiManager.Lib.Model;

namespace MTGViewer.Tests.Utils.Dtos;

internal class RulingDto : IRuling
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
