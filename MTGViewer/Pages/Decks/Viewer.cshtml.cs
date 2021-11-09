using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

namespace MTGViewer.Pages.Decks;


public class ViewerModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;

    public ViewerModel(UserManager<CardUser> userManager, CardDbContext dbContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
    }


    public bool IsOwner { get; private set; }

    public Deck Deck { get; private set; }

    public IEnumerable<QuantityGroup> Cards { get; private set; }


    public async Task<IActionResult> OnGetAsync(int id)
    {
        var deck = await DeckForViewer(id).SingleOrDefaultAsync();

        if (deck == default)
        {
            return NotFound();
        }

        var userId = _userManager.GetUserId(User);

        IsOwner = deck.OwnerId == userId;
        Deck = deck;
        Cards = DeckCardGroups(deck).ToList();

        return Page();
    }


    private IQueryable<Deck> DeckForViewer(int deckId)
    {
        return _dbContext.Decks
            .Where(d => d.Id == deckId)

            .Include(d => d.Owner)

            .Include(d => d.Cards) // unbounded: keep eye on
                .ThenInclude(ca => ca.Card)

            .Include(d => d.Wants) // unbounded: keep eye on
                .ThenInclude(w => w.Card)

            .Include(d => d.GiveBacks) // unbounded: keep eye on
                .ThenInclude(g => g.Card)

            .Include(d => d.TradesTo
                .OrderBy(t => t.Id)
                .Take(1))

            .AsSplitQuery()
            .AsNoTrackingWithIdentityResolution();
    }


    private IEnumerable<QuantityGroup> DeckCardGroups(Deck deck)
    {
        var amountsById = deck.Cards
            .ToDictionary(ca => ca.CardId);

        var wantsById = deck.Wants
            .ToDictionary(w => w.CardId);

        var givesById = deck.GiveBacks
            .ToDictionary(g => g.CardId);

        var cardIds = amountsById.Keys
            .Union(wantsById.Keys)
            .Union(givesById.Keys);

        return cardIds
            .Select(cid =>
                new QuantityGroup(
                    amountsById.GetValueOrDefault(cid),
                    wantsById.GetValueOrDefault(cid),
                    givesById.GetValueOrDefault(cid) ))

            .OrderBy(rg => rg.Card.Name);
    }
}