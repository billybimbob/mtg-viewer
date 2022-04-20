namespace MTGViewer.Data;

public enum SaveResult
{
    None,
    Success,
    Error
}

internal enum LocationType
{
    Invalid,
    Deck,
    Unclaimed,
    Box,
    Excess
}

internal enum QuantityType
{
    Invalid,
    Hold,
    Want,
    Giveback
}
