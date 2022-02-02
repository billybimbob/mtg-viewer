using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;


[Authorize]
public class ResetModel : PageModel
{
    private readonly BulkOperations _bulkOperations;

    public ResetModel(BulkOperations bulkOperations)
    {
        _bulkOperations = bulkOperations;
    }


    public void OnGet()
    {
    }
}