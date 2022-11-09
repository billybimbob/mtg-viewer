using System.Text.Json.Serialization;

using MtgApiManager.Lib.Model;

namespace MtgViewer.Tests.Utils.Dtos;

internal class RulingDto : IRuling
{
    [JsonPropertyName("date")]
    public required string Date { get; set; }

    [JsonPropertyName("text")]
    public required string Text { get; set; }
}
