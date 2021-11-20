using System.ComponentModel.DataAnnotations;
using System.IO;
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

    private readonly JsonCardStorage _jsonStorage;
    private readonly long _fileLimit;


    public BackupModel(JsonCardStorage jsonStorage, IConfiguration config)
    {
        _jsonStorage = jsonStorage;
        _fileLimit = config.GetValue("FileLimit", _2_MB);
    }

    [TempData]
    public string? PostMessage { get; set; }

    public int NumberOfSections { get; private set; }


    [BindProperty(SupportsGet = true)]
    public Export? Export { get; set; }


    [BindProperty]
    public Import? Import { get; set; }


    public async Task OnGetAsync()
    {
        // might be expensive
        NumberOfSections = await _jsonStorage.GetTotalPagesAsync();
    }


    public async Task<IActionResult> OnGetDownloadAsync()
    {
        if (Export is null || Export.Section < 0)
        {
            return NotFound();
        }

        NumberOfSections = await _jsonStorage.GetTotalPagesAsync();

        var section = Export.Section switch
        {
            int page and >0 when page > NumberOfSections => NumberOfSections,
            int page and >0 => page,
            _ => 1
        };

        var cardData = await _jsonStorage.GetFileDataAsync(section - 1);

        return File(cardData, "application/json", $"cardsSection{section}.json");
    }


    public async Task<IActionResult> OnPostUploadAsync()
    {
        if (!ModelState.IsValid || Import is null)
        {
            return Page();
        }

        var file = Import.File;
        var fileKey = $"{nameof(Import)}.{nameof(Import.File)}";

        if (file.Length > _fileLimit)
        {
            ModelState.AddModelError(fileKey, "File is too large to upload");
            return Page();
        }

        var ext = Path.GetExtension(file.FileName).ToLower();

        if (ext != ".json")
        {
            ModelState.AddModelError(fileKey, "File is not a .json file");
            return Page();
        }

        var success = await _jsonStorage.AddFromJsonAsync(file);

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
}