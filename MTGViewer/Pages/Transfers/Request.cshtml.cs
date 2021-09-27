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

        public IReadOnlyList<RequestNameGroup> Requests { get; private set; }


        public async Task<IActionResult> OnGetAsync(int deckId)
        {
            var deck = await DeckWithTakesAndTradesTo(deckId)
                .AsNoTrackingWithIdentityResolution()
                .SingleOrDefaultAsync();

            if (deck == default)
            {
                return NotFound();
            }

            if (!deck.Requests.Any(cr => !cr.IsReturn))
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

            Requests = deck.Requests
                .Where(cr => !cr.IsReturn)
                .GroupBy(ca => ca.Card.Name,
                    (_, takes) => new RequestNameGroup(takes))
                .ToList();

            return Page();
        }


        private IQueryable<Deck> DeckWithTakesAndTradesTo(int deckId)
        {
            var userId = _userManager.GetUserId(User);

            return _dbContext.Decks
                .Where(d => d.Id == deckId && d.OwnerId == userId)

                .Include(d => d.Requests
                    .Where(cr => !cr.IsReturn))
                    .ThenInclude(cr => cr.Card)

                .Include(d => d.TradesTo
                    .OrderBy(da => da.Card.Name))
                    .ThenInclude(ca => ca.Card);
        }


        private IQueryable<CardAmount> RequestTargetsFor(Deck deck)
        {
            var requestNames = deck.Requests
                .Where(ex => !ex.IsReturn)
                .Select(ca => ca.Card.Name)
                .Distinct()
                .ToArray();

            return _dbContext.Amounts
                .Where(ca => ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != deck.OwnerId
                    && requestNames.Contains(ca.Card.Name))

                .Include(ca => ca.Card)
                .Include(ca => ca.Location)
                    .ThenInclude(l => (l as Deck).Owner);
        }



        public async Task<IActionResult> OnPostAsync(int deckId)
        {
            var deck = await DeckWithTakesAndTradesTo(deckId)
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

            var trades = await CreateTradesAsync(deck);

            if (!trades.Any())
            {
                PostMessage = "There are no possible decks to trade with";
                return RedirectToPage("./Index");
            }

            _dbContext.Trades.AttachRange(trades);

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


        private async Task<IEnumerable<Trade>> CreateTradesAsync(Deck deck)
        {
            if (deck.Requests.All(cr => cr.IsReturn))
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
            // TODO: prioritize requesting from exact card matches

            var takes = deck.Requests.Where(cr => !cr.IsReturn);

            var requestMatches = targets
                .GroupJoin( takes,
                    target => target.Card.Name,
                    take => take.Card.Name,
                    (target, takeMatches) =>
                        (target, amount: takeMatches.Sum(cr => cr.Amount)));

            // var requestGroups = deck.ExchangesTo
            //     .Where(ex => !ex.IsTrade)
            //     .GroupBy(ex => ex.Card.Name,
            //         (_, requests) => new ExchangeNameGroup(requests));

            // var requestMatches = requestGroups
            //     .Join( targets,
            //         group => group.Name,
            //         target => target.Card.Name,
            //         (group, Target) => (Target, group.Amount));

            var newTrades = requestMatches
                .Select(ta => new Trade
                {
                    Card = ta.target.Card,
                    To = deck,
                    From = (Deck) ta.target.Location,
                    Amount = ta.amount
                });
                
            return newTrades.ToList();
        }
    }
}