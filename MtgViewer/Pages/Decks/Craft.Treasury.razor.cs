using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Decks;

public partial class Craft
{
    private string? _seek;
    private SeekDirection _direction;

    internal string? Search { get; private set; }

    internal Color PickedColors { get; private set; }

    internal SeekList<HeldCard> Treasury { get; private set; } = SeekList.Empty<HeldCard>();

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

            _seek = null;
            _direction = SeekDirection.Forward;

            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await ApplyFiltersAsync(dbContext);
        }
        finally
        {
            _isBusy = false;
        }
    }

    internal async Task SeekPageAsync(SeekRequest<HeldCard> request)
    {
        string? seek = request.Origin?.Card.Id;
        var direction = request.Direction;

        if (_isBusy || (_seek == seek && _direction == direction))
        {
            return;
        }

        _isBusy = true;

        try
        {
            _seek = seek;
            _direction = direction;

            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await ApplyFiltersAsync(dbContext);
        }
        finally
        {
            _isBusy = false;
        }
    }

    internal async Task ToggleColorAsync(Color value)
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;

        try
        {
            PickedColors ^= value;

            _seek = null;
            _direction = SeekDirection.Forward;

            await using var dbContext = await DbFactory.CreateDbContextAsync(_cancel.Token);

            await ApplyFiltersAsync(dbContext);
        }
        finally
        {
            _isBusy = false;
        }
    }

    internal async Task ApplyFiltersAsync(CardDbContext dbContext)
    {
        var textFilter = ParseTextFilter.Parse(Search);

        dbContext.Cards.AttachRange(_cards);

        Treasury = await FilteredCardsAsync(dbContext, textFilter, _cancel.Token);

        _cards.UnionWith(dbContext.Cards.Local);
    }

    private async Task<SeekList<HeldCard>> FilteredCardsAsync(
        CardDbContext dbContext,
        TextFilter textFilter,
        CancellationToken cancel)
    {
        var cards = dbContext.Cards.AsQueryable();

        string? name = textFilter.Name?.ToUpperInvariant();
        string? text = textFilter.Text?.ToUpperInvariant();

        string[] types = textFilter.Types?.ToUpperInvariant().Split() ?? Array.Empty<string>();

        if (!string.IsNullOrWhiteSpace(name))
        {
            cards = cards
                .Where(c => c.Name.ToUpper().Contains(name));
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

        return await cards
            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(card =>
                new HeldCard(
                    card,
                    card.Holds
                        .Where(h => h.Location is Box || h.Location is Excess)
                        .Sum(h => h.Copies)))

            .SeekBy(_seek, _direction, PageSize.Current)
            .ToSeekListAsync(cancel);
    }
}
