using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Services;

namespace MTGViewer.Pages.Treasury;


[Authorize]
public class ExportModel : PageModel
{
    private readonly FileCardStorage _fileStorage;
    private readonly UserManager<CardUser> _userManager;

    public ExportModel(FileCardStorage fileStorage, UserManager<CardUser> userManager)
    {
        _fileStorage = fileStorage;
        _userManager = userManager;
    }


    public enum DataScope
    {
        User,
        Treasury,
        Complete
    }


    [BindProperty]
    [Display(Name = "Backup Type")]
    public DataScope BackupType { get; set; }


    public void OnGet()
    { }


    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = _userManager.GetUserId(User);
        if (userId is null)
        {
            return NotFound();
        }

        var backup = BackupType switch // file stream should close backup
        {
            DataScope.User => await _fileStorage.GetUserBackupAsync(userId, cancel),
            DataScope.Treasury => await _fileStorage.GetTreasuryBackupAsync(cancel),
            DataScope.Complete or _ => await _fileStorage.GetDefaultBackupAsync(cancel)
        };

        var userName = _userManager.GetDisplayName(User);
        var timestamp = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture);

        string filename = BackupType switch
        {
            DataScope.User => $"cards-{timestamp}-{userName}.json",
            DataScope.Treasury => $"cards-{timestamp}-treasury.json",
            DataScope.Complete or _ => $"cards-{timestamp}-complete.json"
        };

        return File(backup, "application/json",  filename);
    }
}