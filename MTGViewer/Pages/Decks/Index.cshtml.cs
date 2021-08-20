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
        public record DeckColor(Deck Deck, IEnumerable<string> Colors) { }


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
        public IReadOnlyList<CardAmount> Requests { get; private set; }

        public CardAmount PickedRequest { get; private set; }
        public IReadOnlyList<Deck> RequestSources { get; private set; }


        public async Task OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);

            var decks = await _dbContext.Decks
                .Where(l => l.OwnerId == user.Id)
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                        .ThenInclude(c => c.Colors)
                .AsSplitQuery()
                .ToListAsync();

            var colors = decks
                .Select(d => d
                    .GetColors()
                    .Select(c => Color.COLORS[c.Name.ToLower()]));

            var requests = await _dbContext.Amounts
                .Where(ca => ca.IsRequest
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId == user.Id)
                .Include(ca => ca.Card)
                .Include(ca => ca.Location)
                .Distinct()
                .ToListAsync();

            CardUser = user;

            DeckColors = decks
                .Zip(colors, (d, cs) => new DeckColor(d, cs))
                .ToList();

            Requests = requests;
        }


        public async Task<IActionResult> OnPostRequestAsync(int requestId)
        {
            var request = await _dbContext.Amounts
                .Include(ca => ca.Location)
                .Include(ca => ca.Card)
                .FirstOrDefaultAsync(ca => ca.Id == requestId);

            if (request == default || request.Location is not Deck)
            {
                return NotFound();
            }

            var user = await _userManager.GetUserAsync(User);

            var possibleSources = _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == request.CardId
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId != user.Id)
                .Include(ca => ca.Location)
                    .ThenInclude(l => (l as Deck).Owner);

            var sources = await possibleSources
                .GroupJoin( _dbContext.Trades,
                    amount => 
                        new { amount.CardId, DeckId = amount.LocationId },
                    trade =>
                        new { trade.CardId, DeckId = trade.FromId },
                    (amount, trades) =>
                        new { amount, trades })
                .SelectMany(
                    ats =>
                        ats.trades.DefaultIfEmpty(),
                    (ats, trade) =>
                        new { ats.amount, trade })
                .Where(at => at.trade == default)
                .Select(at => at.amount)
                .ToListAsync();
            
            CardUser = user;

            PickedRequest = request;

            RequestSources = sources
                .Select(ca => ca.Location as Deck)
                .Distinct()
                .ToList();

            return Page();
        }


        public async Task<IActionResult> OnPostTradeAsync(int requestId, int deckId, int amount)
        {
            var request = await _dbContext.Amounts
                .Include(ca => ca.Location)
                .FirstOrDefaultAsync(ca => ca.Id == requestId);

            if (request == default || request.Location is not Deck)
            {
                return NotFound();
            }

            var target = await _dbContext.Decks
                .Include(d => d.Owner)
                .FirstOrDefaultAsync(d => d.Id == deckId);

            if (target == default)
            {
                return NotFound();
            }
            
            var user = await _userManager.GetUserAsync(User);

            var isPriorRequest = await _dbContext.Trades
                .Where(t => t.CardId == request.CardId
                    && t.ToId == request.LocationId
                    && t.ProposerId == user.Id)
                .AnyAsync();

            if (isPriorRequest)
            {
                PostMessage = "Selected deck is already requested";
                return RedirectToPage("./Index");
            }

            var targetAmount = await _dbContext.Amounts
                .AsNoTracking()
                .FirstOrDefaultAsync(ca =>
                    !ca.IsRequest
                        && ca.CardId == request.CardId
                        && ca.LocationId == target.Id);

            if (targetAmount == default)
            {
                PostMessage = "Target deck is no longer valid";
                return RedirectToPage("./Index");
            }

            var upperLimit = System.Math.Min(request.Amount, targetAmount.Amount);

            amount = amount switch
            {
                < 1 => 1,
                _ when amount > upperLimit => upperLimit,
                _ => amount
            };

            var trade = new Trade
            {
                Card = request.Card,
                Proposer = user,
                Receiver = target.Owner,
                From = target,
                To = (Deck)request.Location,
                Amount = amount
            };

            _dbContext.Trades.Attach(trade);

            try
            {
                await _dbContext.SaveChangesAsync();
                PostMessage = "Request successfully made";
            }
            catch (DbUpdateConcurrencyException)
            {
                PostMessage = "Ran into issue while creating request";
            }
            catch (DbUpdateException)
            {
                PostMessage = "Ran into issue while creating request";
            }

            return RedirectToPage("./Index");
        }
    }
}