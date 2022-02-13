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
    Box,
    Unclaimed,
    Deck,
}


internal enum QuantityType
{
    Invalid,
    Amount,
    Want,
    GiveBack,
}
