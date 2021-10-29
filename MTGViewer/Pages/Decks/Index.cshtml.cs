using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;


namespace MTGViewer.Pages.Decks
{
    public enum State
    {
        Theorycraft,
        Built,
        Requesting
    }

    public record DeckState(Deck Deck, State State)
    {
        public DeckState(Deck deck) : this(deck, State.Theorycraft)
        {
            if (deck.TradesTo.Any())
            {
                State = State.Requesting;
            }
            else if (deck.Wants.Any() || deck.GiveBacks.Any())
            {
                State = State.Theorycraft;
            }
            else
            {
                State = State.Built;
            }
        }
    }


    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly UserManager<CardUser> _userManager;
        private readonly CardDbContext _dbContext;
        private readonly int _pageSize;

        public IndexModel(
            UserManager<CardUser> userManager, CardDbContext dbContext, PageSizes pageSizes)
        {
            _userManager = userManager;
            _dbContext = dbContext;
            _pageSize = pageSizes.GetSize<IndexModel>();
        }


        [TempData]
        public string PostMessage { get; set; }

        public UserRef CardUser { get; private set; }
        public PagedList<DeckState> Decks { get; private set; }


        public async Task OnGetAsync(int? pageIndex)
        {
            var userId = _userManager.GetUserId(User);

            Decks = await DeckStates(userId)
                .ToPagedListAsync(_pageSize, pageIndex);

            CardUser = Decks.FirstOrDefault()?.Deck.Owner
                ?? await _dbContext.Users.FindAsync(userId);
        }


        private IQueryable<DeckState> DeckStates(string userId)
        {
            return _dbContext.Decks
                .Where(d => d.OwnerId == userId)

                .Include(d => d.Owner)

                .Include(d => d.Cards) // unbounded: keep eye on
                    .ThenInclude(ca => ca.Card)

                .Include(d => d.Wants
                    .OrderBy(w => w.Id)
                    .Take(1))

                .Include(d => d.GiveBacks
                    .OrderBy(gb => gb.Id)
                    .Take(1))

                .Include(d => d.TradesTo
                    .OrderBy(t => t.Id)
                    .Take(1))

                .OrderBy(d => d.Name)
                .Select(deck => new DeckState(deck))
                .AsSplitQuery();
        }
    }
}