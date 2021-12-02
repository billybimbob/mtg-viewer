using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using MTGViewer.Data;

namespace MTGViewer.Pages.Treasury;


[Authorize]
public class CreateModel : PageModel
{
    private readonly CardDbContext _dbContext;
    private readonly ILogger<CreateModel> _logger;

    public CreateModel(CardDbContext dbContext, ILogger<CreateModel> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }


    [TempData]
    public string? PostMessage { get; set; }

    [BindProperty]
    public Box? Box { get; set; }

    public IReadOnlyList<Bin> Bins { get; private set; } = Array.Empty<Bin>();


    public async Task OnGetAsync(CancellationToken cancel)
    {
        Bins = await OrderedBinsAsync(cancel);
    }


    private Task<List<Bin>> OrderedBinsAsync(CancellationToken cancel) =>
        _dbContext.Bins
            .OrderBy(b => b.Name)
            .AsNoTrackingWithIdentityResolution()
            .ToListAsync(cancel); // unbounded: keep eye on


    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        if (Box is null)
        {
            ModelState.AddModelError(string.Empty, "Box model is not valid");

            return await PageWithBinsAsync(cancel);
        }

        _dbContext.Boxes.Attach(Box);

        if (Box.BinId != default)
        {
            await _dbContext.Entry(Box)
                .Reference(b => b.Bin)
                .LoadAsync(cancel);
        }

        ModelState.ClearValidationState(nameof(Box));

        if (!TryValidateModel(Box, nameof(Box)))
        {
            return await PageWithBinsAsync(cancel);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancel);
            return RedirectToPage("Index");
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, "Ran into issue while creating new box");

            return await PageWithBinsAsync(cancel);
        }
    }


    private async Task<PageResult> PageWithBinsAsync(CancellationToken cancel)
    {
        Bins = await OrderedBinsAsync(cancel);

        return Page();
    }
}