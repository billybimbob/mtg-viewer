namespace MTGViewer.Data.Internal;

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
    Excess,
}


internal enum QuantityType
{
    Invalid,
    Amount,
    Want,
    GiveBack,
}
