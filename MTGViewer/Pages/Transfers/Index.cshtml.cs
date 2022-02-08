using System;
using System.Collections.Paging;
using System.Collections.Generic;
using System.Linq;
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

    public SeekList<Deck> TradeDecks { get; private set; } = SeekList<Deck>.Empty();

    public IReadOnlyList<Suggestion> Suggestions { get; private set; } = Array.Empty<Suggestion>();



    public async Task<IActionResult> OnGetAsync(int? seek, bool backTrack, CancellationToken cancel)
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

        TradeDecks = await GetDecksAsync(userId, seek, backTrack, cancel);

        Suggestions = await SuggestionsForIndex(userId).ToListAsync(cancel);

        return Page();
    }


    public IQueryable<Deck> DecksForIndex(string userId)
    {
        return _dbContext.Decks
            .Where(d => d.OwnerId == userId)

            .Where(d => d.TradesFrom.Any()
                || d.TradesTo.Any()
                || d.Wants.Any())

            .Include(d => d.TradesFrom
                .OrderBy(t => t.Id)
                .Take(1))

            .Include(d => d.TradesTo
                .OrderBy(t => t.Id)
                .Take(1))

            .Include(d => d.Wants
                .OrderBy(w => w.Id)
                .Take(1))

            .OrderBy(d => d.Name)
                .ThenBy(d => d.Id)

            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution();
    }


    private IQueryable<Suggestion> SuggestionsForIndex(string userId)
    {
        return _dbContext.Suggestions
            .Where(s => s.ReceiverId == userId)

            .Include(s => s.Card)
            .Include(s => s.To)

            .OrderByDescending(s => s.SentAt)
                .ThenBy(s => s.Card.Name)
                .ThenBy(s => s.Id)

            .Take(_pageSize)

            .AsNoTrackingWithIdentityResolution();
    }


    private async Task<SeekList<Deck>> GetDecksAsync(
        string userId, 
        int? seek, 
        bool backTrack, 
        CancellationToken cancel)
    {
        var userDecks = DecksForIndex(userId);

        if (seek is null)
        {
            return await userDecks
                .ToSeekListAsync(SeekPosition.Start, _pageSize, cancel);
        }

        var deck = await _dbContext.Decks
            .OrderBy(d => d.Id)
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == seek, cancel);

        if (deck == default)
        {
            return await userDecks
                .ToSeekListAsync(SeekPosition.Start, _pageSize, cancel);
        }

        return backTrack

            ? await userDecks
                .ToSeekListAsync(d =>
                    d.Name == deck.Name && d.Id < deck.Id
                        || d.Name.CompareTo(deck.Name) < 0,
                        
                    SeekPosition.End, _pageSize, cancel)

            : await userDecks
                .ToSeekListAsync(d =>
                    d.Name == deck.Name && d.Id < deck.Id
                        || d.Name.CompareTo(deck.Name) < 0,
                    
                    SeekPosition.Start, _pageSize, cancel);
    }


    public async Task<IActionResult> OnPostAsync(int suggestId, CancellationToken cancel)
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
                s.Id == suggestId && s.ReceiverId == userId, cancel);

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