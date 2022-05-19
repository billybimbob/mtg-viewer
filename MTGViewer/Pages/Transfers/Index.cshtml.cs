using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Data.Projections;
using MTGViewer.Services;

namespace MTGViewer.Pages.Transfers;

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

    public SeekList<TradeDeckPreview> TradeDecks { get; private set; } = SeekList<TradeDeckPreview>.Empty;

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

        TradeDecks = await TradingDecks(userId)
            .SeekBy(seek, direction)
            .OrderBy<Deck>()
            .Take(_pageSize.Current)
            .ToSeekListAsync(cancel);

        Suggestions = await SuggestionsAsync
            .Invoke(_dbContext, userId, _pageSize.Current)
            .ToListAsync(cancel);

        return Page();
    }

    public IQueryable<TradeDeckPreview> TradingDecks(string userId)
    {
        return _dbContext.Decks
            .Where(d => d.OwnerId == userId
                && (d.TradesFrom.Any() || d.TradesTo.Any() || d.Wants.Any()))

            .OrderBy(d => d.Name)
                .ThenBy(d => d.Id)

            .Select(d => new TradeDeckPreview
            {
                Id = d.Id,
                Name = d.Name,
                Color = d.Color,

                SentTrades = d.TradesTo.Any(),

                // trades are only valid if the From target (Hold) exists
                ReceivedTrades = d.TradesFrom.Any() && d.Holds.Any(),

                WantsCards = d.Wants.Any()
            });
    }

    private static readonly Func<CardDbContext, string, int, IAsyncEnumerable<SuggestionPreview>> SuggestionsAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string userId, int limit) =>

            dbContext.Suggestions
                .Where(s => s.ReceiverId == userId)

                .OrderByDescending(s => s.SentAt)
                    .ThenBy(s => s.Card.Name)
                    .ThenBy(s => s.Id)

                .Take(limit)
                .Select(s => new SuggestionPreview
                {
                    Id = s.Id,
                    SentAt = s.SentAt,

                    CardId = s.CardId,
                    CardName = s.Card.Name,
                    CardManaCost = s.Card.ManaCost,

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
