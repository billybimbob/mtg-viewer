using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;


[Authorize]
public class ExportModel : PageModel
{
    private readonly FileCardStorage _fileStorage;
    private readonly BulkOperations _bulkOperations;

    public ExportModel(FileCardStorage fileStorage, BulkOperations bulkOperations)
    {
        _fileStorage = fileStorage;
        _bulkOperations = bulkOperations;
    }


    public sealed class DownloadModel
    {
        [Display(Name = "Section To Download")]
        [Range(0, int.MaxValue)]
        public int? Section { get; set; }
    }


    [TempData]
    public string? PostMessage { get; set; }

    public int NumberOfSections { get; private set; }

    [BindProperty]
    public DownloadModel? Download { get; set; }


    public async Task OnGetAsync(CancellationToken cancel)
    {
        // might be expensive
        NumberOfSections = await _bulkOperations.GetTotalPagesAsync(cancel);
    }


    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        if (Download is null || Download.Section < 0)
        {
            return NotFound();
        }

        NumberOfSections = await _bulkOperations.GetTotalPagesAsync(cancel);

        if (Download.Section > NumberOfSections)
        {
            return NotFound();
        }

        int section = Download.Section switch
        {
            int page => page,
            _ => 1
        };

        var cardData = await _fileStorage.GetBackupAsync(section - 1, cancel);

        return File(cardData, "application/json", $"CardsSection{section}.json");
    }
}