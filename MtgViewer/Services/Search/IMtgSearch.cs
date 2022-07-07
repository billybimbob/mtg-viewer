using MtgViewer.Data;

namespace MtgViewer.Services.Search;

public interface IMtgSearch
{
    bool IsEmpty { get; }

    string? Name { get; }

    int? ManaValue { get; }

    Color Colors { get; }

    Rarity? Rarity { get; }

    string? SetName { get; }

    string? Types { get; }

    string? Artist { get; }

    string? Power { get; }

    string? Toughness { get; }

    string? Loyalty { get; }

    string? Text { get; }

    string? Flavor { get; }

    int Page { get; }

    int PageSize { get; }
}
