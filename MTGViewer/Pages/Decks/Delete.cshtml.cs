using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

#nullable enable
namespace MTGViewer.Pages.Decks;

[Authorize]
public class DeleteModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly ITreasury _treasury;
    private readonly ILogger<DeleteModel> _logger;

    public DeleteModel(
        UserManager<CardUser> userManager,
        CardDbContext dbContext,
        ITreasury treasury,
        ILogger<DeleteModel> logger)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _treasury = treasury;
        _logger = logger;
    }


    [TempData]
    public string? PostMesssage { get; set; }

    public Deck? Deck { get; private set; }
    public IReadOnlyList<QuantityNameGroup>? NameGroups { get; private set; }

    public IReadOnlyList<Trade>? Trades { get; private set; }


    public async Task<IActionResult> OnGetAsync(int id)
    {
        var deck = await DeckForDelete(id).SingleOrDefaultAsync();

        if (deck == default)
        {
            return NotFound();
        }

        var deckTrades = deck.TradesTo
            .Concat(deck.TradesFrom)
            .OrderBy(t => t.Card.Name)
            .ToList();

        Deck = deck;

        NameGroups = DeckNameGroup(deck).ToList();

        Trades = deckTrades;

        return Page();
    }


    private IQueryable<Deck> DeckForDelete(int deckId)
    {
        var userId = _userManager.GetUserId(User);

        return _dbContext.Decks
            .Where(d => d.Id == deckId && d.OwnerId == userId)

            .Include(d => d.Cards) // unbounded: keep eye on
                .ThenInclude(da => da.Card)

            .Include(d => d.Wants) // unbounded: keep eye on
                .ThenInclude(w => w.Card)

            .Include(d => d.GiveBacks) // unbounded: keep eye on
                .ThenInclude(g => g.Card)

            .Include(d => d.TradesTo) // unbounded: keep eye on
                .ThenInclude(t => t.Card)
            .Include(d => d.TradesTo)
                .ThenInclude(t => t.From)

            .Include(d => d.TradesFrom) // unbounded: keep eye on
                .ThenInclude(t => t.Card)
            .Include(d => d.TradesFrom)
                .ThenInclude(t => t.To)

            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution();
    }


    private IEnumerable<QuantityNameGroup> DeckNameGroup(Deck deck)
    {
        var amountsByName = deck.Cards
            .ToLookup(ca => ca.Card.Name);

        var wantsByName = deck.Wants
            .ToLookup(w => w.Card.Name);

        var givesByName = deck.GiveBacks
            .ToLookup(g => g.Card.Name);

        var cardNames = amountsByName.Select(an => an.Key)
            .Union(wantsByName.Select(wn => wn.Key))
            .Union(givesByName.Select(gn => gn.Key))
            .OrderBy(cn => cn);

        return cardNames.Select(cn =>
            new QuantityNameGroup(
                amountsByName[cn], wantsByName[cn], givesByName[cn] ));
    }



    public async Task<IActionResult> OnPostAsync(int id)
    {
        var userId = _userManager.GetUserId(User);

        var deck = await _dbContext.Decks
            .Include(d => d.Cards)
                .ThenInclude(da => da.Card)
            .FirstOrDefaultAsync(d =>
                d.Id == id && d.OwnerId == userId);

        if (deck == default)
        {
            return RedirectToPage("Index");
        }

        var returningCards = deck.Cards
            // no source, since deck is being deleted
            .Select(da => new CardReturn(da.Card, da.NumCopies))
            .ToList();

        _dbContext.Amounts.RemoveRange(deck.Cards);
        _dbContext.Wants.RemoveRange(deck.Wants);
        _dbContext.GiveBacks.RemoveRange(deck.GiveBacks);

        _dbContext.Decks.Remove(deck);

        try
        {
            if (returningCards.Any())
            {
                await _treasury.ReturnAsync(returningCards);
            }

            await _dbContext.SaveChangesAsync();

            PostMesssage = $"Successfully deleted {deck.Name}";
        }
        catch (DbUpdateException)
        {
            PostMesssage = $"Ran into issue while trying to delete {deck.Name}";
        }

        return RedirectToPage("./Index");
    }
}