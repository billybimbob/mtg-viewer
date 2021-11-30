using System;
using System.Linq;
using System.Collections.Generic;
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


    public async Task OnGetAsync()
    {
        Bins = await OrderedBinsAsync();
    }


    private Task<List<Bin>> OrderedBinsAsync() =>
        _dbContext.Bins
            .OrderBy(b => b.Name)
            .AsNoTrackingWithIdentityResolution()
            .ToListAsync(); // unbounded: keep eye on


    public async Task<IActionResult> OnPostAsync()
    {
        if (Box is null)
        {
            ModelState.AddModelError(string.Empty, "Box model is not valid");

            return await PageWithBinsAsync();
        }

        _dbContext.Boxes.Attach(Box);

        if (Box.BinId != default)
        {
            await _dbContext.Entry(Box)
                .Reference(b => b.Bin)
                .LoadAsync();
        }

        ModelState.ClearValidationState(nameof(Box));

        if (!TryValidateModel(Box, nameof(Box)))
        {
            return await PageWithBinsAsync();
        }

        try
        {
            await _dbContext.SaveChangesAsync();
            return RedirectToPage("Index");
        }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, "Ran into issue while creating new box");

            return await PageWithBinsAsync();
        }
    }


    private async Task<PageResult> PageWithBinsAsync()
    {
        Bins = await OrderedBinsAsync();

        return Page();
    }
}