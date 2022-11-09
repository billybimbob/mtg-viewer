
namespace MtgViewer.Services.Seed;

public class SeedSettings
{
    public required int Seed { get; set; } = 100;

    public required string FilePath { get; set; } = "cards";

    public required string? Password { get; set; }
}
