using MtgApiManager.Lib.Service;

namespace MTGViewer.Services.Search;

internal interface IMtgParameter
{
    bool IsEmpty { get; }

    IMtgParameter Accept(object? value);

    ICardService Apply(ICardService cards);
}
