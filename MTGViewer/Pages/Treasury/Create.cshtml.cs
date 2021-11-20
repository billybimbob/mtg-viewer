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
        Bins = await _dbContext.Bins
            .OrderBy(b => b.Name)
            .AsNoTrackingWithIdentityResolution()
            .ToListAsync(); // unbounded: keep eye on
    }


    public async Task<IActionResult> OnPostAsync()
    {
        if (Box is null)
        {
            ModelState.AddModelError(string.Empty, "Box model is not valid");
            return Page();
        }

        if (Box.BinId != default)
        {
            var bin = await _dbContext.Bins.FindAsync(Box.BinId);

            if (bin is null)
            {
                var binIdKey = $"{nameof(Box)}.{nameof(Box.BinId)}";
                ModelState.AddModelError(binIdKey, "Bin specified is not valid");

                return Page();
            }

            Box.Bin = bin;
        }

        ModelState.ClearValidationState(nameof(Box));

        if (!TryValidateModel(Box, nameof(Box)))
        {
            return Page();
        }

        _dbContext.Boxes.Attach(Box);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            PostMessage = "Ran into issue while creating new box";
        }

        return RedirectToPage("Index");
    }
}