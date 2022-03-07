using System;
using System.Collections.Generic;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Transfers;


[Authorize]
public class IndexModel : PageModel
{
    private readonly int _pageSize;
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly IAuthorizationService _authorizations;

    public IndexModel(
        PageSizes pageSizes, 
        UserManager<CardUser> userManager, 
        CardDbContext dbContext,
        IAuthorizationService authorizations)
    {
        _pageSize = pageSizes.GetPageModelSize<IndexModel>();
        _userManager = userManager;
        _dbContext = dbContext;
        _authorizations = authorizations;
    }


    [TempData]
    public string? PostMessage { get; set; }

    public string UserName { get; private set; } = string.Empty;

    public SeekList<DeckTradePreview> TradeDecks { get; private set; } = SeekList<DeckTradePreview>.Empty;

    public IReadOnlyList<SuggestionPreview> Suggestions { get; private set; } = Array.Empty<SuggestionPreview>();



    public async Task<IActionResult> OnGetAsync(
        int? seek,
        bool backtrack,
        CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return NotFound();
        }

        var userName = await _dbContext.Users
            .Where(u => u.Id == userId)
            .Select(u => u.Name)
            .SingleOrDefaultAsync(cancel);

        if (userName is null)
        {
            return NotFound();
        }

        UserName = userName;

        TradeDecks = await DecksForIndex(userId)
            .SeekBy(_pageSize, backtrack)
            .WithOrigin<Deck>()
            .WithKey(seek)
            .ToSeekListAsync(cancel);

        Suggestions = await SuggestionsForIndex(userId).ToListAsync(cancel);

        return Page();
    }


    public IQueryable<DeckTradePreview> DecksForIndex(string userId)
    {
        return _dbContext.Decks
            .Where(d => d.OwnerId == userId
                && (d.TradesFrom.Any() || d.TradesTo.Any() || d.Wants.Any()))

            .OrderBy(d => d.Name)
                .ThenBy(d => d.Id)

            .Select(d => new DeckTradePreview
            {
                Id = d.Id,
                Name = d.Name,
                Color = d.Color,

                SentTrades = d.TradesTo.Any(),
                ReceivedTrades = d.TradesFrom.Any(),
                WantsCards = d.Wants.Any()
            });
    }


    private IQueryable<SuggestionPreview> SuggestionsForIndex(string userId)
    {
        return _dbContext.Suggestions
            .Where(s => s.ReceiverId == userId)
            .Select(s => new SuggestionPreview
            {
                Id = s.Id,
                SentAt = s.SentAt,

                CardId = s.CardId,
                CardName = s.Card.Name,
                CardManaCost = s.Card.ManaCost,
                
                ToName = s.To == null ? null : s.To.Name,
                Comment = s.Comment
            })

            .OrderByDescending(s => s.SentAt)
                .ThenBy(s => s.CardName)
                .ThenBy(s => s.Id)

            .Take(_pageSize);
    }



    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        var userId = _userManager.GetUserId(User);
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