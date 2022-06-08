using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using EntityFrameworkCore.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Cards;

public class DetailsModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public DetailsModel(CardDbContext dbContext, PageSize pageSize)
    {
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    public Card Card { get; private set; } = default!;

    public IReadOnlyList<CardLink> Alternatives { get; private set; } = Array.Empty<CardLink>();

    public SeekList<QuantityLocationPreview> Locations { get; private set; } = SeekList<QuantityLocationPreview>.Empty;

    public string? ReturnUrl { get; private set; }

    private IEnumerable<KeyValuePair<string, string?>> CardParameters
    {
        get
        {
            var invariant = CultureInfo.InvariantCulture;

            yield return KeyValuePair.Create(nameof(Create.Name), (string?)Card.Name);

            yield return KeyValuePair.Create(nameof(Create.Colors), ((int?)Card.Color)?.ToString(invariant));

            yield return KeyValuePair.Create(nameof(Create.Cmc), Card.ManaValue?.ToString(invariant));

            yield return KeyValuePair.Create(nameof(Create.ReturnUrl), ReturnUrl);
        }
    }

    public string GetCreateCardUri()
        => QueryHelpers.AddQueryString("/Cards/Create", CardParameters);

    public async Task<IActionResult> OnGetAsync(
        string id,
        bool flip,
        string? returnUrl,
        int? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        var card = await GetCardAsync(id, flip, cancel);

        if (card is null)
        {
            return NotFound();
        }

        var locations = await GetLocationsAsync(card.Id, seek, direction, cancel);

        if (!locations.Any() && seek is not null)
        {
            return RedirectToPage(new
            {
                flip,
                returnUrl,
                seek = null as int?,
                direction = SeekDirection.Forward,
            });
        }

        Card = card;

        Alternatives = await CardAlternativesAsync
            .Invoke(_dbContext, card.Id, card.Name, _pageSize.Limit)
            .ToListAsync(cancel);

        Locations = locations;

        if (Url.IsLocalUrl(returnUrl))
        {
            ReturnUrl = returnUrl;
        }

        return Page();
    }

    private Task<Card?> GetCardAsync(string cardId, bool flip, CancellationToken cancel)
    {
        var cardQuery = _dbContext.Cards
            .Where(c => c.Id == cardId)
            .OrderBy(c => c.Id)
            .AsNoTrackingWithIdentityResolution();

        if (flip)
        {
            cardQuery = cardQuery
                .Include(c => c.Flip);
        }
        return cardQuery
            .SingleOrDefaultAsync(cancel);
    }

    private static readonly Func<CardDbContext, string, string, int, IAsyncEnumerable<CardLink>> CardAlternativesAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, string cardName, int limit) =>
            dbContext.Cards
                .Where(c => c.Id != cardId && c.Name == cardName)
                .OrderBy(c => c.SetName)
                .Take(limit)
                .Select(c => new CardLink
                {
                    Id = c.Id,
                    Name = c.Name,
                    SetName = c.SetName
                }));

    private Task<SeekList<QuantityLocationPreview>> GetLocationsAsync(
        string cardId,
        int? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        return _dbContext.Holds
            .Where(h => h.CardId == cardId)
            .OrderBy(h => h.Location.Name)
                .ThenBy(h => h.LocationId)

            .Select(h => new QuantityLocationPreview
            {
                Location = new LocationPreview
                {
                    Id = h.LocationId,
                    Name = h.Location.Name,
                    Type = h.Location.Type
                },
                Copies = h.Copies
            })

            .SeekBy(seek, direction)
            .OrderBy<Hold>()
            .Take(_pageSize.Current)

            .ToSeekListAsync(cancel);
    }
}
