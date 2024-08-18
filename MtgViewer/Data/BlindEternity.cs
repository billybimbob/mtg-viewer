namespace MtgViewer.Data;

/// <summary>
/// Hacky storage option to represent cards that were deleted
/// TODO: allow for changes to have a null to so that this can be removed
/// </summary>
public class BlindEternity : Storage
{
    public static BlindEternity Create() => new() { Name = "Blind Eternities", };
}
