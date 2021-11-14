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
    public IFormFile File { get; set; }
}


public class Export
{
    [Display(Name = "Page To Download")]
    [Range(0, int.MaxValue)]
    public int? Page { get; set; }
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
    public string PostMessage { get; set; }

    [BindProperty]
    public Import Import { get; set; }

    [BindProperty]
    public Export Export { get; set; }

    public void OnGet()
    { }


    public async Task<IActionResult> OnPostDownloadAsync()
    {
        var pageIndex = Export.Page switch
        {
            int page and >0 => page - 1,
            _ => 0
        };

        var cardData = await _jsonStorage.GetFileDataAsync(pageIndex);

        return File(cardData, "application/json", "cards.json");
    }


    public async Task<IActionResult> OnPostUploadAsync()
    {
        ModelState.Remove(nameof(Export.Page));

        if (!ModelState.IsValid)
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