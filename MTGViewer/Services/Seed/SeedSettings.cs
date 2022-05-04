
namespace MTGViewer.Services.Seed;

public class SeedSettings
{
    public int Seed { get; set; } = 100;

    public string FilePath { get; set; } = "cards";

    public string? Password { get; set; }
}
