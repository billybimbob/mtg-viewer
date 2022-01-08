using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Transfers;


[Authorize]
public class SuggestModel : PageModel
{
    private readonly int _pageSize;
    private readonly CardDbContext _dbContext;
    private readonly UserManager<CardUser> _userManager;

    public SuggestModel(
        PageSizes pageSizes,
        CardDbContext dbContext, 
        UserManager<CardUser> userManager)
    {
        _pageSize = pageSizes.GetPageModelSize<SuggestModel>();
        _dbContext = dbContext;
        _userManager = userManager;
    }


    public Card Card { get; private set; } = null!;

    public PagedList<UserRef> Users { get; private set; } = PagedList<UserRef>.Empty;


    public async Task<IActionResult> OnGetAsync(string cardId, int? pageIndex, CancellationToken cancel)
    {
        if (cardId is null)
        {
            return NotFound();
        }

        var card = await _dbContext.Cards.FindAsync(new []{ cardId }, cancel);
        if (card is null)
        {
            return NotFound();
        }

        Card = card;

        Users = await UsersForSuggest(card)
            .ToPagedListAsync(_pageSize, pageIndex, cancel);

        return Page();
    }


    public IQueryable<UserRef> UsersForSuggest(Card card)
    {
        var proposerId = _userManager.GetUserId(User);

        var nonProposers = _dbContext.Users
            .Where(u => u.Id != proposerId);

        var cardSuggests = _dbContext.Suggestions
            .Where(s => s.Card.Name == card.Name && s.ToId == default);

        return nonProposers
            .GroupJoin( cardSuggests,
                user => user.Id,
                suggest => suggest.ReceiverId,
                (user, suggests) =>
                    new { user, suggests })
            .SelectMany(
                uss => uss.suggests.DefaultIfEmpty(),
                (uss, suggest) =>
                    new { uss.user, suggest })

            .Where(us => us.suggest == default)
            .Select(us => us.user)
            .OrderBy(u => u.Name)
            .AsNoTracking();
    }


    public IActionResult OnPost(string cardId, string receiverId)
    {
        if (cardId == null || receiverId == null)
        {
            return RedirectToPage();
        }

        return RedirectToPage("SuggestUser", new { cardId, receiverId });
    }
}