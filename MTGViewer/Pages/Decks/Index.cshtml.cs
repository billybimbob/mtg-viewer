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
        public IEnumerable<DeckColor> DeckColors { get; private set; }
        public IEnumerable<CardAmount> Requests { get; private set; }

        public CardAmount PickedRequest { get; private set; }
        public IEnumerable<Deck> RequestSources { get; private set; }


        public async Task OnGet()
        {
            CardUser = await _userManager.GetUserAsync(User);

            var decks = await _dbContext.Decks
                .Where(l => l.Owner == CardUser)
                .Include(l => l.Cards)
                    .ThenInclude(ca => ca.Card)
                        .ThenInclude(c => c.Colors)
                .AsSplitQuery()
                .ToListAsync();

            var colors = decks
                .Select(d => d
                    .GetColors()
                    .Select(c => Color.COLORS[c.Name.ToLower()]));

            DeckColors = decks.Zip(colors, (d, cs) => new DeckColor(d, cs));

            Requests = await _dbContext.Amounts
                .Where(ca => ca.IsRequest
                    && ca.Location is Deck
                    && (ca.Location as Deck).OwnerId == CardUser.Id)
                .Distinct()
                .ToListAsync();
        }


        public async Task<IActionResult> OnPostRequestAsync(int requestId)
        {
            PickedRequest = await _dbContext.Amounts.FindAsync(requestId);

            if (PickedRequest == null || PickedRequest.Location is not Deck)
            {
                return NotFound();
            }

            CardUser = await _userManager.GetUserAsync(User);

            var possibleSources = await _dbContext.Amounts
                .Where(ca => !ca.IsRequest
                    && ca.CardId == PickedRequest.CardId
                    && ca.Location is Deck)
                .Select(ca => ca.Location as Deck)
                .Where(d => d.OwnerId == CardUser.Id)
                .Distinct()
                .ToListAsync();

            var alreadyRequested = await _dbContext.Trades
                .Where(t => t.CardId == PickedRequest.CardId
                    && t.ToId == PickedRequest.LocationId
                    && t.ProposerId == CardUser.Id)
                .Select(t => t.From)
                .Distinct()
                .ToListAsync();

            RequestSources = possibleSources.Except(alreadyRequested);

            return Page();
        }


        public async Task<IActionResult> OnPostDeckAsync(int requestId, int deckId)
        {
            var request = await _dbContext.Amounts.FindAsync(requestId);

            if (request == null || request.Location is not Deck)
            {
                return NotFound();
            }

            var target = await _dbContext.Decks
                .Include(d => d.Owner)
                .FirstOrDefaultAsync(d => d.Id == deckId);

            if (target == null)
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

            var trade = new Trade
            {
                Card = request.Card,
                Proposer = user,
                Receiver = target.Owner,
                From = target,
                To = (Deck)request.Location,
                Amount = request.Amount
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