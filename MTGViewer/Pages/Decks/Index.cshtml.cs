using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;


namespace MTGViewer.Pages.Decks
{
    [Authorize]
    public class IndexModel : PageModel
    {
        public enum DeckState
        {
            Invalid,
            Valid,
            Requesting
        }

        public record DeckColor(Deck Deck, IEnumerable<string> Colors, DeckState State) { }


        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;

        public IndexModel(UserManager<CardUser> userManager, CardDbContext context)
        {
            _userManager = userManager;
            _dbContext = context;
        }


        [TempData]
        public string PostMessage { get; set; }

        public CardUser CardUser { get; private set; }
        public IReadOnlyList<DeckColor> DeckColors { get; private set; }

        // public IReadOnlyList<CardAmount> Requests { get; private set; }
        // public CardAmount PickedRequest { get; private set; }
        // public IReadOnlyList<Deck> RequestSources { get; private set; }


        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);

            var decks = await _dbContext.Decks
                .Where(d => d.OwnerId == user.Id)
                .Include(d => d.Cards)
                    .ThenInclude(ca => ca.Card)
                        .ThenInclude(c => c.Colors)
                .AsSplitQuery()
                .ToListAsync();

            var colors = decks
                .Select(d => d
                    .GetColors()
                    .Select(c => Color.COLORS[c.Name.ToLower()]));

            var states = GetDeckStates(
                decks, await GetRequestingAsync(user));

            // var requests = await _dbContext.Amounts
            //     .Where(ca => ca.IsRequest
            //         && ca.Location is Deck
            //         && (ca.Location as Deck).OwnerId == user.Id)
            //     .Include(ca => ca.Card)
            //     .Include(ca => ca.Location)
            //     .ToListAsync();

            CardUser = user;

            DeckColors = decks
                .Zip(colors, (deck, color) => (deck, color))
                .Zip(states, (dc, state) => new DeckColor(dc.deck, dc.color, state))
                .ToList();

            // Requests = requests;
        }


        private async Task<IReadOnlyList<Deck>> GetRequestingAsync(CardUser user)
        {
            var userDecks = _dbContext.Decks
                .Where(d => d.OwnerId == user.Id);

            var userTrades = _dbContext.Trades
                .Where(t => t.ProposerId == user.Id);

            return await userDecks
                .Join( userTrades,
                    d => d.Id, t => t.ToId, (deck, trade) => deck)
                .Distinct()
                .ToListAsync();
        }


        private IEnumerable<DeckState> GetDeckStates(IEnumerable<Deck> decks, IEnumerable<Deck> requestDecks)
        {
            requestDecks = requestDecks.ToHashSet();

            foreach(var deck in decks)
            {
                if (requestDecks.Contains(deck))
                {
                    yield return DeckState.Requesting;
                }
                else if (deck.Cards.Any(ca => ca.IsRequest))
                {
                    yield return DeckState.Invalid;
                }
                else
                {
                    yield return DeckState.Valid;
                }
            }
        }


        /*
        public async Task<IActionResult> OnPostRequestAsync(int requestId)
        {
            var request = await GetRequestInfoAsync(requestId);

            if (request == default)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);

            if (user == default)
            {
                return NotFound();
            }
            
            CardUser = user;
            PickedRequest = request;
            RequestSources = await GetRequestOptionsAsync(request, user);

            return Page();
        }


        public async Task<IActionResult> OnPostSubmitAsync(int requestId, int deckId, int amount)
        {
            var request = await GetRequestInfoAsync(requestId);

            if (request == default)
            {
                return NotFound();
            }
            
            var user = await _userManager.GetUserAsync(User);

            if (user == null)
            {
                return NotFound();
            }

            var target = await GetTargetDeckAsync(deckId);

            if (target == default)
            {
                PostMessage = "Deck selected is invalid";
                // possibly redirect?
                return await OnPostRequestAsync(requestId);
            }

            amount = await GetAmountAsync(request, target, user, amount);

            if (amount == default)
            {
                return RedirectToPage("./Index");
            }

            var trade = new Trade
            {
                Card = request.Card,
                Proposer = user,
                Receiver = target.Owner,
                To = (Deck)request.Location,
                From = target,
                Amount = amount
            };

            _dbContext.Trades.Attach(trade);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Request successfully made";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into issue while creating request";
            }

            return RedirectToPage("./Index");
        }



        private async Task<CardAmount> GetRequestInfoAsync(int requestId)
        {
            if (requestId == default)
            {
                return default;
            }

            return await _dbContext.Amounts
                .Include(ca => ca.Card)
                .Include(ca => ca.Location)
                .SingleOrDefaultAsync(ca => 
                    ca.Id == requestId && ca.Location is Deck);
        }


        private async Task<IReadOnlyList<Deck>> GetRequestOptionsAsync(
            CardAmount request, CardUser user)
        {
            var possibleSources = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == request.CardId
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != user.Id)
                .Include(ca => ca.Location)
                    .ThenInclude(l => (l as Deck).Owner);

            var validSources = await possibleSources
                Join( _dbContext.Trades,
                    amount => 
                        new { amount.CardId, DeckId = amount.LocationId },
                    trade =>
                        new { trade.CardId, DeckId = trade.FromId },
                    (amount, trade) => amount)
                .ToListAsync();

            return validSources
                .Select(ca => ca.Location as Deck)
                .Distinct()
                .ToList();
        }


        private async Task<Deck> GetTargetDeckAsync(int deckId)
        {
            if (deckId == default)
            {
                return default;
            }

            return await _dbContext.Decks
                .Include(d => d.Owner)
                .SingleOrDefaultAsync(d => d.Id == deckId);
        }


        private async Task<int> GetAmountAsync(
            CardAmount request, Deck target, CardUser user, int amount)
        {
            var isPriorRequest = await _dbContext.Trades
                .Where(t => t.CardId == request.CardId
                    && t.ToId == request.LocationId
                    && t.FromId == target.Id
                    && t.ProposerId == user.Id)
                .AnyAsync();

            if (isPriorRequest)
            {
                PostMessage = "Selected deck is already requested";
                return default;
            }

            var targetAmount = await _dbContext.Amounts
                .AsNoTracking()
                .SingleOrDefaultAsync(ca =>
                    !ca.IsRequest
                        && ca.CardId == request.CardId
                        && ca.LocationId == target.Id);

            if (targetAmount == default)
            {
                PostMessage = "Target deck is no longer valid";
                return default;
            }

            var upperLimit = Math.Min(request.Amount, targetAmount.Amount);

            return amount switch
            {
                < 1 => 1,
                _ when upperLimit > 0 && upperLimit < amount => upperLimit,
                _ => amount
            };
        }
        */
    }
}