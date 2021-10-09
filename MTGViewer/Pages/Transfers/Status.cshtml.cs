using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

#nullable enable

namespace MTGViewer.Pages.Transfers
{
    [Authorize]
    public class StatusModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public StatusModel(UserManager<CardUser> userManager, CardDbContext dbContext)
        {
            _userManager = userManager;
            _dbContext = dbContext;
        }


        [TempData]
        public string? PostMessage { get; set; }

        public Deck? Deck { get; private set; }

        public IReadOnlyList<QuantityNameGroup>? CardGroups { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            if (deckId == default)
            {
                return NotFound();
            }

            var deck = await DeckForStatus(deckId).SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (!deck.Wants.Any())
            {
                PostMessage = $"There are no requests for {deck.Name}";
                return RedirectToPage("Index");
            }

            if (!deck.TradesTo.Any())
            {
                return RedirectToPage("Request", new { deckId });
            }

            CapToTrades(deck);

            Deck = deck;
            CardGroups = CardNameGroups(deck).ToList();

            return Page();
        }


        private IQueryable<Deck> DeckForStatus(int deckId)
        {
            var userId = _userManager.GetUserId(User);
            
            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)

                .Include(d => d.Owner)

                .Include(d => d.Cards) // unbounded: keep eye on
                    .ThenInclude(ca => ca.Card)

                .Include(d => d.Wants) // unbounded: keep eye on
                    .ThenInclude(w => w.Card)

                .Include(d => d.TradesTo)
                    .ThenInclude(t => t.Card)

                .Include(d => d.TradesTo)
                    .ThenInclude(t => t.From.Owner)

                .Include(d => d.TradesTo)
                    .ThenInclude(t => t.From.Cards) // unbounded: keep eye on
                        .ThenInclude(ca => ca.Card)

                .Include(d => d.TradesTo // unbounded: keep eye on
                    .OrderBy(t => t.From.Owner.Name)
                        .ThenBy(t => t.Card.Name))

                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution();
        }


        private void CapToTrades(Deck deck)
        {
            var fromTargets = deck.TradesTo
                .SelectMany(t => t.From.Cards)
                .Distinct();

            var tradesWithAmountCap = deck.TradesTo
                .GroupJoin( fromTargets,
                    t => (t.CardId, t.FromId),
                    ca => (ca.CardId, ca.LocationId),
                    (trade, targets) => (trade, targets))
                .SelectMany(
                    tts => tts.targets.DefaultIfEmpty(),
                    (tts, target) => (tts.trade, target?.Amount ?? 0));

            // modifications are not saved

            foreach (var (trade, cap) in tradesWithAmountCap)
            {
                trade.Amount = Math.Min(trade.Amount, cap);
            }

            deck.TradesTo.RemoveAll(t => t.Amount == 0);
        }


        private IEnumerable<QuantityNameGroup> CardNameGroups(Deck deck)
        {
            var amountsByName = deck.Cards
                .ToLookup(ca => ca.Card.Name);

            var wantsByName = deck.Wants
                .ToLookup(w => w.Card.Name);

            var cardNames = amountsByName.Select(an => an.Key)
                .Union(wantsByName.Select(wn => wn.Key))
                .OrderBy(cn => cn);

            return cardNames.Select(cn => 
                new QuantityNameGroup(
                    amountsByName[cn], wantsByName[cn] ));
        }



        public async Task<IActionResult> OnPostAsync(int deckId)
        {
            if (deckId == default)
            {
                PostMessage = "Deck is not valid";
                return RedirectToPage("Index");
            }

            var userId = _userManager.GetUserId(User);

            // keep eye on, could possibly remove trades not started by the user
            // makes the assumption that trades are always started by the owner of the To deck
            var deck = await _dbContext.Decks
                .Include(d => d.TradesTo)
                .SingleOrDefaultAsync(d =>
                    d.Id == deckId && d.OwnerId == userId);

            if (deck == default)
            {
                PostMessage = "Deck is not valid";
                return RedirectToPage("Index");
            }

            if (!deck.TradesTo.Any())
            {
                PostMessage = "No trades were found";
                return RedirectToPage("Index");
            }

            _dbContext.Trades.RemoveRange(deck.TradesTo);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Successfully cancelled requests";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into error while cancelling";
            }

            return RedirectToPage("Index");
        }
    }
}