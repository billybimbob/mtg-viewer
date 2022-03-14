using System;
using System.Linq;
using System.Paging;
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
[Authorize(Policy = CardPolicies.ChangeTreasury)]
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


    public CardPreview Card { get; private set; } = default!;

    public OffsetList<UserRef> Users { get; private set; } = OffsetList<UserRef>.Empty;


    public async Task<IActionResult> OnGetAsync(string id, int? offset, CancellationToken cancel)
    {
        var card = await CardAsync.Invoke(_dbContext, id, cancel);

        if (card is null)
        {
            return NotFound();
        }

        var users = await UsersForSuggest(card)
            .PageBy(offset, _pageSize)
            .ToOffsetListAsync(cancel);

        if (users.Offset.Current > users.Offset.Total)
        {
            return RedirectToPage(new { offset = null as int? });
        }

        Card = card;
        Users = users;

        return Page();
    }


    private static readonly Func<CardDbContext, string, CancellationToken, Task<CardPreview?>> CardAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, string cardId, CancellationToken _) =>
            dbContext.Cards
                .Select(c => new CardPreview
                {
                    Id = c.Id,
                    Name = c.Name,
                    SetName = c.SetName,

                    ManaCost = c.ManaCost,
                    Rarity = c.Rarity,
                    ImageUrl = c.ImageUrl
                })
                .SingleOrDefault(c => c.Id == cardId));


    private IQueryable<UserRef> UsersForSuggest(CardPreview card)
    {
        var proposerId = _userManager.GetUserId(User);

        var nonProposers = _dbContext.Users
            .Where(u => u.Id != proposerId)
            .OrderBy(u => u.Name)
                .ThenBy(u => u.Id)
            .AsNoTracking();

        var cardSuggests = _dbContext.Suggestions
            .Where(s => s.Card.Name == card.Name && s.ReceiverId != proposerId);

        return nonProposers
            .GroupJoin( cardSuggests,
                user => user.Id,
                suggest => suggest.ReceiverId,
                (User, Suggests) => new { User, Suggests })

            .SelectMany(
                uss => uss.Suggests.DefaultIfEmpty(),
                (uss, Suggest) => new { uss.User, Suggest })

            .Where(us => us.Suggest == default)
            .Select(us => us.User);
    }


    public IActionResult OnPost(string id, string receiverId)
    {
        if (id is null || receiverId is null)
        {
            return RedirectToPage();
        }

        return RedirectToPage("Offer", new { id, receiverId });
    }
}