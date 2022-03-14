using System;
using System.Linq;
using System.Paging;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Decks;


public class DetailsModel : PageModel
{
    private readonly UserManager<CardUser> _userManager;
    private readonly CardDbContext _dbContext;
    private readonly int _pageSize;

    public DetailsModel(
        UserManager<CardUser> userManager, 
        CardDbContext dbContext,
        PageSizes pageSizes)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _pageSize = pageSizes.GetPageModelSize<DetailsModel>();
    }


    public bool IsOwner { get; private set; }

    public DeckDetails Deck { get; private set; } = default!;

    public SeekList<DeckCopies> Cards { get; private set; } = SeekList<DeckCopies>.Empty;


    public async Task<IActionResult> OnGetAsync(
        int id,
        string? seek,
        SeekDirection direction,
        CancellationToken cancel)
    {
        var deck = await DeckDetailsAsync.Invoke(_dbContext, id, cancel);

        if (deck == default)
        {
            return NotFound();
        }

        var cards = await DeckCards(id)
            .SeekBy(seek, direction)
            .OrderBy<Card>()
            .Take(_pageSize)
            .ToSeekListAsync(cancel);

        var userId = _userManager.GetUserId(User);

        IsOwner = deck.Owner.Id == userId;
        Deck = deck;
        Cards = cards;

        return Page();
    }


    private static readonly Func<CardDbContext, int, CancellationToken, Task<DeckDetails?>> DeckDetailsAsync
        = EF.CompileAsyncQuery((CardDbContext dbContext, int deckId, CancellationToken _) =>
            dbContext.Decks
                .Where(d => d.Id == deckId)
                .Select(d => new DeckDetails
                {
                    Id = d.Id,
                    Name = d.Name,
                    Color = d.Color,

                    Owner = new OwnerPreview
                    {
                        Id = d.OwnerId,
                        Name = d.Owner.Name
                    },

                    AmountCopies = d.Cards.Sum(a => a.Copies),
                    WantCopies = d.Wants.Sum(w => w.Copies),
                    ReturnCopies = d.GiveBacks.Sum(g => g.Copies),

                    HasTrades = d.TradesTo.Any()
                })
                .SingleOrDefault());


    private IQueryable<DeckCopies> DeckCards(int deckId)
    {
        return _dbContext.Cards
            .Where(c => c.Amounts.Any(a => a.LocationId == deckId)
                || c.Wants.Any(w => w.LocationId == deckId)
                || c.GiveBacks.Any(g => g.LocationId == deckId))

            .OrderBy(c => c.Name)
                .ThenBy(c => c.SetName)
                .ThenBy(c => c.Id)

            .Select(c => new DeckCopies
            {
                Id = c.Id,
                Name = c.Name,

                SetName = c.SetName,
                ManaCost = c.ManaCost,

                Rarity = c.Rarity,
                ImageUrl = c.ImageUrl,

                Held = c.Amounts
                    .Where(a => a.LocationId == deckId)
                    .Sum(a => a.Copies),

                Want = c.Wants
                    .Where(w => w.LocationId == deckId)
                    .Sum(w => w.Copies),

                Returning = c.GiveBacks
                    .Where(g => g.LocationId == deckId)
                    .Sum(g => g.Copies),
            });
    }

}