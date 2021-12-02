using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;


public class Import
{
    [Required]
    [Display(Name = "Card Data File")]
    public IFormFile File { get; set; } = null!;
}


public class Export
{
    [Display(Name = "Section To Download")]
    [Range(0, int.MaxValue)]
    public int? Section { get; set; }
}


[Authorize]
public class BackupModel : PageModel
{
    private const long _2_MB = 2_097_152;

    private readonly FileCardStorage _fileStorage;
    private readonly long _fileLimit;


    public BackupModel(FileCardStorage fileStorage, IConfiguration config)
    {
        _fileStorage = fileStorage;
        _fileLimit = config.GetValue("FileLimit", _2_MB);
    }

    [TempData]
    public string? PostMessage { get; set; }

    public int NumberOfSections { get; private set; }


    [BindProperty(SupportsGet = true)]
    public Export? Export { get; set; }


    [BindProperty]
    public Import? Import { get; set; }


    public async Task OnGetAsync(CancellationToken cancel)
    {
        // might be expensive
        NumberOfSections = await _fileStorage.GetTotalPagesAsync(cancel);
    }


    public async Task<IActionResult> OnGetDownloadAsync(CancellationToken cancel)
    {
        if (Export is null || Export.Section < 0)
        {
            return NotFound();
        }

        NumberOfSections = await _fileStorage.GetTotalPagesAsync(cancel);

        var section = Export.Section switch
        {
            int page and >0 when page > NumberOfSections => NumberOfSections,
            int page and >0 => page,
            _ => 1
        };

        var cardData = await _fileStorage.GetFileDataAsync(section - 1, cancel);

        return File(cardData, "application/json", $"cardsSection{section}.json");
    }


    public async Task<IActionResult> OnPostUploadAsync(CancellationToken cancel)
    {
        if (!ModelState.IsValid || Import is null)
        {
            return await PageWithTotalPageAsync(cancel);
        }

        const string fileKey = $"{nameof(Import)}.{nameof(Import.File)}";

        var file = Import.File;

        if (file.Length > _fileLimit)
        {
            ModelState.AddModelError(fileKey, "File is too large to upload");

            return await PageWithTotalPageAsync(cancel);
        }

        string ext = Path.GetExtension(file.FileName).ToLower();

        if (ext != ".json" && ext != ".csv")
        {
            ModelState.AddModelError(fileKey, "File is not a .json or .csv file");

            return await PageWithTotalPageAsync(cancel);
        }

        // TODO: fix bug where existing card additions always fail to add

        bool success = ext switch
        {
            ".json" => await _fileStorage.TryJsonAddAsync(file, cancel),
            ".csv" => await _fileStorage.TryCsvAddAsync(file, cancel),
            _ => false
        };

        if (success)
        {
            PostMessage = "Successfully added data from file";
        }
        else
        {
            PostMessage = "Ran into issue while trying to add file data";
        }

        return RedirectToPage("Index");
    }


    private async Task<PageResult> PageWithTotalPageAsync(CancellationToken cancel)
    {
        NumberOfSections = await _fileStorage.GetTotalPagesAsync(cancel);

        return Page();
    }
}