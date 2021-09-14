using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Transfers
{
    [Authorize]
    public class RequestModel : PageModel
    {
        private CardDbContext _dbContext;
        private UserManager<CardUser> _userManager;
        private ILogger<RequestModel> _logger;

        public RequestModel(
            CardDbContext dbContext, UserManager<CardUser> userManager, ILogger<RequestModel> logger)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _logger = logger;
        }


        [TempData]
        public string PostMessage { get; set; }

        public bool TargetsExist { get; private set; }

        public Deck Deck { get; private set; }

        public IReadOnlyList<CardNameGroup> Requests { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await DeckWithRequests(userId, deckId)
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (!deck.Cards.Any())
            {
                PostMessage = "There are no possible requests";
                return RedirectToPage("./Index");
            }

            if (deck.TradesTo.Any())
            {
                return RedirectToPage("./Status", new { deckId });
            }


            TargetsExist = await RequestTargetsFor(deck).AnyAsync();

            Deck = deck;

            Requests = deck.Cards
                .GroupBy(ca => ca.Card.Name,
                    (_, amounts) => new CardNameGroup(amounts))
                .ToList();

            return Page();
        }


        private IQueryable<Deck> DeckWithRequests(string userId, int deckId)
        {
            var userDeck = _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId);

            var withCardRequests = userDeck
                .Include(d => d.Cards
                    .Where(ca => ca.IsRequest))
                    .ThenInclude(ca => ca.Card);

            var withTradeRequests = withCardRequests
                .Include(d => d.TradesTo
                    .Where(t => t.ProposerId == userId))
                .AsSplitQuery();

            return withTradeRequests;
        }


        private IQueryable<CardAmount> RequestTargetsFor(Deck deck)
        {
            var deckIncludes = _dbContext.DeckAmounts
                .Include(ca => ca.Card)
                .Include(ca => ca.Deck)
                    .ThenInclude(d => d.Owner);

            var requestNames = deck.Cards
                .Select(ca => ca.Card.Name)
                .Distinct()
                .ToArray();

            return deckIncludes
                .Where(ca => !ca.IsRequest
                    && ca.Deck.OwnerId != deck.OwnerId
                    && requestNames.Contains(ca.Card.Name));
        }



        public async Task<IActionResult> OnPostAsync(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await DeckWithRequests(userId, deckId)
                .SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (deck.TradesTo.Any())
            {
                PostMessage = "Request is already sent";
                return RedirectToPage("./Index");
            }

            var tradeRequests = await CreateTradeRequestsAsync(deck);

            if (!tradeRequests.Any())
            {
                PostMessage = "There are no possible decks to request to";
                return RedirectToPage("./Index");
            }

            _dbContext.Trades.AttachRange(tradeRequests);

            try
            {
                await _dbContext.SaveChangesAsync();

                PostMessage = "Request was successfully sent";
                return RedirectToPage("./Status", new { deckId });
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into issue while trying to send request";
                return RedirectToPage("./Index");
            }
        }


        private async Task<IEnumerable<Trade>> CreateTradeRequestsAsync(Deck deck)
        {
            // deck.Cards will only be requests
            if (!deck.Cards.Any())
            {
                return Enumerable.Empty<Trade>();
            }

            var requestTargets = await RequestTargetsFor(deck).ToListAsync();

            if (!requestTargets.Any())
            {
                return Enumerable.Empty<Trade>();
            }

            return FindTradeMatches(deck, requestTargets);
        }


        private IReadOnlyList<Trade> FindTradeMatches(Deck deck, IEnumerable<CardAmount> targets)
        {
            // TODO: figure out how to query more on server
            var requestGroups = deck.Cards
                .GroupBy(ca => ca.Card.Name,
                    (_, amounts) => new CardNameGroup(amounts));

            var requestMatches = requestGroups
                .Join( targets,
                    group => group.Name,
                    target => target.Card.Name,
                    (group, target) => 
                        // TODO: prioritize requesting from exact card matches
                        (target, amount: Math.Min(target.Amount, group.Amount)) );

            var newTrades = requestMatches
                .Select(ta =>
                {
                    var fromDeck = (Deck) ta.target.Location;
                    return new Trade
                    {
                        Card = ta.target.Card,
                        Proposer = deck.Owner,
                        Receiver = fromDeck.Owner,
                        To = deck,
                        From = fromDeck,
                        Amount = ta.amount
                    };
                });
            
            return newTrades.ToList();
        }
    }
}