using System;

using MtgApiManager.Lib.Service;

namespace MtgViewer.Services.Search.Parameters;

internal interface IMtgParameter : IEquatable<IMtgParameter>
{
    bool IsEmpty { get; }

    IMtgParameter From(object? value);

    ICardService ApplyTo(ICardService cards);
}
