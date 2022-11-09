namespace MtgViewer.Services;

public class MulliganOptions
{
    /// <summary>
    /// Max hand size of starting mulligan
    /// </summary>
    public required int HandSize { get; set; } = 7;

    /// <summary>
    /// Refresh time in milliseconds
    /// </summary>
    public required int DrawInterval { get; set; } = 300;
}
