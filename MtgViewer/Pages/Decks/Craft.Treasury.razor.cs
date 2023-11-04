using System;
using System.Linq;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Decks;

public partial class Craft
{
    private string? _search;
    private TextFilter? _filter;

    internal SeekList<HeldCard> Treasury { get; private set; } = SeekList.Empty<HeldCard>();

    internal Color PickedColors { get; private set; }

    internal string? Search
    {
        get => _search;
        private set
        {
            _search = value;
            _filter = null;
        }
    }

    internal TextFilter Filter => _filter ??= ParseTextFilter.Parse(Search);

    internal async Task SearchAsync(string? value)
    {
        if (_isBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            value = null;
        }

        const StringComparison ignoreCase = StringComparison.CurrentCultureIgnoreCase;

        if (value?.Length > TextFilter.Limit
            || string.Equals(value, Search, ignoreCase))
        {
            return;
        }

        _isBusy = true;

        try
        {
            Search = value;

            await ApplyFiltersAsync();
        }
        finally
        {
            _isBusy = false;
        }
    }

    internal async Task ChangeColorAsync(Color value)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            PickedColors = value;

            await ApplyFiltersAsync();
        }
        finally
        {
            _isBusy = false;
        }
    }

    internal async Task SeekPageAsync(SeekRequest<HeldCard> request)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            dbContext.Cards.AttachRange(_cards);

            var (origin, direction) = request;

            Treasury = await FetchTreasuryAsync(dbContext, origin, direction);
        }
        finally
        {
            _isBusy = false;
        }
    }

    private async Task ApplyFiltersAsync()
    {
        await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

        const SeekDirection forward = SeekDirection.Forward;

        dbContext.Cards.AttachRange(_cards);

        if (DeckCraft is DeckCraft.Built)
        {
            DeckHolds = await FetchQuantitiesAsync(dbContext, null as Hold, forward);
            DeckReturns = await FetchQuantitiesAsync(dbContext, null as Giveback, forward);
        }
        else
        {
            DeckWants = await FetchQuantitiesAsync(dbContext, null as Want, forward);
            Treasury = await FetchTreasuryAsync(dbContext, null, forward);
        }
    }

    private async Task<SeekList<HeldCard>> FetchTreasuryAsync(
        CardDbContext dbContext,
        HeldCard? origin,
        SeekDirection direction)
    {
        var cards = dbContext.Cards.AsQueryable();

        string? name = Filter.Name?.ToUpperInvariant();
        string? text = Filter.Text?.ToUpperInvariant();

        string[] types = Filter.Types?.ToUpperInvariant().Split() ?? Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(name))
        {
            cards = cards
                .Where(c => c.Name.ToUpper().Contains(name));
        }

        if (Filter.Mana is ManaFilter mana)
        {
            cards = cards.Where(mana.CreateFilter());
        }

        if (!string.IsNullOrWhiteSpace(text))
        {
            cards = cards
                .Where(c => c.Text != null
                    && c.Text.ToUpper().Contains(text));
        }

        foreach (string type in types)
        {
            cards = cards
                .Where(c => c.Type.ToUpper().Contains(type));
        }

        if (PickedColors is not Color.None)
        {
            cards = cards
                .Where(c => c.Color.HasFlag(PickedColors));
        }

        string? originId = origin?.Card.Id;

        var treasury = await cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .SeekBy(direction)
                .After(c => c.Id == originId)
                .Take(PageSize.Current)

            .Select(card => new HeldCard(
                card,
                card.Holds
                    .Where(h => h.Location is Box || h.Location is Excess)
                    .Sum(h => h.Copies)))

            .ToSeekListAsync(_cancel.Token);

        _cards.UnionWith(
            treasury.Select(h => h.Card));

        return treasury;
    }
}
