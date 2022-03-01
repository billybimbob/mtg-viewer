using MtgApiManager.Lib.Service;

namespace MTGViewer.Services;

internal interface IMtgParameter
{
    bool IsEmpty { get; }

    IMtgParameter Accept(object? value);

    void Apply(ICardService cards);
}
