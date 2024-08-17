using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

using MtgViewer.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Data.Projections;
using MtgViewer.Services;

namespace MtgViewer.Pages.Decks;

public class ExportModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly BackupFactory _backupFactory;

    public ExportModel(CardDbContext dbContext, BackupFactory backupFactory)
    {
        _dbContext = dbContext;
        _backupFactory = backupFactory;
    }

    public DeckDetails Deck { get; private set; } = default!;

    [BindProperty]
    [Display(Name = "Export Type")]
    public DeckMulligan ExportType { get; set; }

    public async Task<IActionResult> OnGetAsync(int id, CancellationToken cancel)
    {
        var deck = await DeckDetailsAsync.Invoke(_dbContext, id, cancel);

        if (deck is null)
        {
            return NotFound();
        }

        Deck = deck;

        return Page();
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<DeckDetails?>> DeckDetailsAsync
        = EF.CompileAsyncQuery((CardDbContext db, int id, CancellationToken _)
            => db.Decks
                .Where(d => d.Id == id)
                .Select(d => new DeckDetails
                {
                    Id = d.Id,
                    Name = d.Name,
                    Color = d.Color,
                })
                .SingleOrDefault());

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancel)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var deck = await DeckOwnerAsync.Invoke(_dbContext, id, cancel);
        if (deck is null)
        {
            return NotFound();
        }

        var exportData = await _backupFactory.GetDeckExportAsync(id, ExportType, cancel);
        string fileName = $"{deck.Owner.Name}-{deck.Name}-{ExportType}.txt";

        return File(exportData, "text/plain", fileName);
    }

    private static readonly Func<CardDbContext, int, CancellationToken, Task<DeckDetails?>> DeckOwnerAsync
        = EF.CompileAsyncQuery((CardDbContext db, int id, CancellationToken _)
            => db.Decks
                .Where(d => d.Id == id)
                .Select(d => new DeckDetails
                {
                    Id = d.Id,
                    Name = d.Name,
                    Owner = new PlayerPreview
                    {
                        Id = d.Owner.Id,
                        Name = d.Owner.Name
                    }
                })
                .SingleOrDefault());
}

