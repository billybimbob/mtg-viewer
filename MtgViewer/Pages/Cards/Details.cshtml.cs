using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

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

    public SeekList<LocationCopy> Locations { get; private set; } = SeekList.Empty<LocationCopy>();

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
        var card = await FindCardAsync(id, flip, cancel);

        if (card is null)
        {
            return NotFound();
        }

        var locations = await SeekLocationsAsync(card.Id, direction, seek, cancel);

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

    private Task<Card?> FindCardAsync(string cardId, bool flip, CancellationToken cancel)
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
        = EF.CompileAsyncQuery((CardDbContext db, string id, string name, int limit)
            => db.Cards
                .Where(c => c.Id != id && c.Name == name)
                .OrderBy(c => c.SetName)
                .Take(limit)
                .Select(c => new CardLink
                {
                    Id = c.Id,
                    Name = c.Name,
                    SetName = c.SetName
                }));

    private async Task<SeekList<LocationCopy>> SeekLocationsAsync(
        string cardId,
        SeekDirection direction,
        int? origin,
        CancellationToken cancel)
    {
        return await _dbContext.Holds
            .Where(h => h.CardId == cardId)

            .OrderBy(h => h.Location.Name)
                .ThenBy(h => h.LocationId)

            .SeekBy(direction)
                .After(h => h.Id == origin)
                .ThenTake(_pageSize.Current)

            .Select(h => new LocationCopy
            {
                Id = h.LocationId,
                Name = h.Location.Name,
                Type = h.Location.Type,
                Copies = h.Copies
            })

            .ToSeekListAsync(cancel);
    }
}
