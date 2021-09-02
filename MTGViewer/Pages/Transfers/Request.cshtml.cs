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

        public IReadOnlyList<NameGroup> Requests { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await _dbContext.Decks
                .Include(d => d.Cards
                    .Where(ca => ca.IsRequest))
                    .ThenInclude(ca => ca.Card)
                .Include(d => d.ToRequests
                    .Where(t => t is Trade && t.ProposerId == userId))
                .AsSplitQuery()
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync(d => d.Id == deckId && d.OwnerId == userId);

            if (deck == default)
            {
                return NotFound();
            }

            if (!deck.Cards.Any())
            {
                PostMessage = "There are no possible requests";
                return RedirectToPage("./Index");
            }

            if (deck.ToRequests.Any())
            {
                return RedirectToPage("./Status", new { deckId });
            }


            TargetsExist = await AnyTradeRequestsAsync(userId, deckId);

            Deck = deck;

            Requests = deck.Cards
                .GroupBy(ca => ca.Card.Name)
                .Select(g => new NameGroup(g))
                .ToList();

            return Page();
        }


        private async Task<bool> AnyTradeRequestsAsync(string userId, int deckId)
        {
            var possibleAmounts = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != userId);

            var cardRequests = _dbContext.Amounts
                .Where(ca => ca.IsRequest && ca.LocationId == deckId);

            return await possibleAmounts
                .Join( cardRequests,
                    amount => amount.Card.Name,
                    request => request.Card.Name,
                    (amount, request) => amount)
                .AnyAsync();
        }



        public async Task<IActionResult> OnPostAsync(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            var deck = await _dbContext.Decks
                .Include(d => d.Owner)
                .Include(d => d.Cards
                    .Where(ca => ca.IsRequest))
                    .ThenInclude(ca => ca.Card)
                .Include(d => d.ToRequests
                    .Where(t => t is Trade && t.ProposerId == userId))
                .AsSplitQuery()
                .SingleOrDefaultAsync(d => d.Id == deckId && d.OwnerId == userId);

            if (deck == default)
            {
                return NotFound();
            }

            if (deck.ToRequests.Any())
            {
                PostMessage = "Request is already sent";
                return RedirectToPage("./Index");
            }

            var user = deck.Owner;
            var tradeRequests = await FindTradeRequestsAsync(deck);

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


        private async Task<IEnumerable<Trade>> FindTradeRequestsAsync(Deck deck)
        {
            // deck.Cards will only be requests
            if (!deck.Cards.Any())
            {
                return Enumerable.Empty<Trade>();
            }


            var requestNames = deck.Cards
                .Select(ca => ca.Card.Name)
                .Distinct()
                .ToArray();

            var requestTargets = await _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != deck.OwnerId
                    && requestNames.Contains(ca.Card.Name))
                .Include(ca => ca.Card)
                .Include(ca => ca.Location)
                    .ThenInclude(l => (l as Deck).Owner)
                .ToListAsync();

            if (!requestTargets.Any())
            {
                return Enumerable.Empty<Trade>();
            }


            // TODO: figure out how to query more on server

            var requestGroups = deck.Cards
                .GroupBy(
                    ca => ca.Card.Name,
                    (name, cas) =>
                        (name, amount: cas.Select(ca => ca.Amount).Sum()) );

            var requestMatches = requestGroups
                .Join( requestTargets,
                    g => g.name,
                    target => target.Card.Name,
                    (g, target) => (g.name, g.amount, target));

            var dividedAmounts = requestMatches
                .GroupBy(
                    nat => (nat.name, nat.target.LocationId),
                    (_, nats) =>
                    {
                        // amount should be the same for all in nats
                        var targets = nats.Select(nat => nat.target);
                        var totalRequest = nats.First().amount;

                        return GetRequestAmounts(targets, totalRequest);
                    })
                .SelectMany(ta => ta)
                .Where(ta => ta.amount > 0);

            var newTrades = dividedAmounts
                .Select(ta =>
                {
                    var fromDeck = (Deck) ta.target.Location;
                    return new Trade
                    {
                        Card = ta.target.Card,
                        Proposer = deck.Owner,
                        Receiver = fromDeck.Owner,
                        From = fromDeck,
                        To = deck,
                        Amount = Math.Min(ta.target.Amount, ta.amount)
                    };
                });
            
            return newTrades.ToList();
        }


        private IEnumerable<(CardAmount target, int amount)> GetRequestAmounts(
            IEnumerable<CardAmount> targets, int requestTotal)
        {
            var amounts = new List<int>();

            foreach (var target in targets)
            {
                // TODO: prioritize requesting from exact card matches
                if (requestTotal == 0)
                {
                    break;
                }

                var amount = Math.Min(target.Amount, requestTotal);

                amounts.Add(amount);
                requestTotal -= amount;
            }

            amounts.AddRange(
                Enumerable.Repeat(0, targets.Count() - amounts.Count));

            return targets.Zip(amounts);
        }
    }
}