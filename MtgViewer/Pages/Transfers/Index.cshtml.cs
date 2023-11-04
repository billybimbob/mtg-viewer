using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using EntityFrameworkCore.Paging;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Transfers;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IAuthorizationService _authorizations;
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly PageSize _pageSize;

    public IndexModel(
        IAuthorizationService authorizations,
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        PageSize pageSize)
    {
        _authorizations = authorizations;
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSize;
    }

    [TempData]
    public string? PostMessage { get; set; }

    public string UserName { get; private set; } = string.Empty;

    public SeekList<TradeDeckPreview> TradeDecks { get; private set; } = SeekList.Empty<TradeDeckPreview>();

    public IReadOnlyList<SuggestionPreview> Suggestions { get; private set; } = Array.Empty<SuggestionPreview>();

    public async Task<IActionResult> OnGetAsync(int? seek, SeekDirection direction, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return Challenge();
        }

        string? userName = _userManager.GetDisplayName(User);

        if (userName is null)
        {
            return NotFound();
        }

        UserName = userName;

        TradeDecks = await SeekDecksAsync(userId, direction, seek, cancel);

        Suggestions = await SuggestionsAsync
            .Invoke(_dbContext, userId, _pageSize.Current)
            .ToListAsync(cancel);

        return Page();
    }

    public async Task<SeekList<TradeDeckPreview>> SeekDecksAsync(
        string userId,
        SeekDirection direction,
        int? origin,
        CancellationToken cancel)
    {
        return await _dbContext.Decks
            .Where(d => d.OwnerId == userId
                && (d.TradesFrom.Any() || d.TradesTo.Any() || d.Wants.Any()))

            .OrderBy(d => d.Name)
                .ThenBy(d => d.Id)

            .SeekBy(direction)
                .After(d => d.Id == origin)
                .Take(_pageSize.Current)

            .Select(d => new TradeDeckPreview
            {
                Id = d.Id,
                Name = d.Name,
                Color = d.Color,

                SentTrades = d.TradesTo.Any(),

                // trades are only valid if the From target (Hold) exists
                ReceivedTrades = d.TradesFrom.Any() && d.Holds.Any(),

                WantsCards = d.Wants.Any()
            })

            .ToSeekListAsync(cancel);
    }

    private static readonly Func<CardDbContext, string, int, IAsyncEnumerable<SuggestionPreview>> SuggestionsAsync
        = EF.CompileAsyncQuery((CardDbContext db, string receiver, int limit)
            => db.Suggestions
                .Where(s => s.ReceiverId == receiver)

                .OrderByDescending(s => s.SentAt)
                    .ThenBy(s => s.Card.Name)
                    .ThenBy(s => s.Id)

                .Take(limit)
                .Select(s => new SuggestionPreview
                {
                    Id = s.Id,
                    SentAt = s.SentAt,

                    Card = new CardLink
                    {
                        Id = s.CardId,
                        Name = s.Card.Name,
                        SetName = s.Card.SetName,
                        ManaCost = s.Card.ManaCost
                    },

                    ToName = s.To == null ? null : s.To.Name,
                    Comment = s.Comment
                }));

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return NotFound();
        }

        var changeTreasury = await _authorizations.AuthorizeAsync(User, CardPolicies.ChangeTreasury);

        if (!changeTreasury.Succeeded)
        {
            return NotFound();
        }

        var suggestion = await _dbContext.Suggestions
            .SingleOrDefaultAsync(s =>
                s.Id == id && s.ReceiverId == userId, cancel);

        if (suggestion is null)
        {
            PostMessage = "Specified suggestion cannot be acknowledged";

            return RedirectToPage();
        }

        _dbContext.Suggestions.Remove(suggestion);

        try
        {
            await _dbContext.SaveChangesAsync(cancel);

            PostMessage = "Suggestion Acknowledged";
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into issue while trying to Acknowledge";
        }

        return RedirectToPage();
    }
}
