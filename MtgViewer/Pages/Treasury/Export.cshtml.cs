using System;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using MtgViewer.Areas.Identity.Data;
using MtgViewer.Data.Infrastructure;
using MtgViewer.Services;

namespace MtgViewer.Pages.Treasury;

[Authorize]
public class ExportModel : PageModel
{
    private readonly BackupFactory _backupFactory;
    private readonly UserManager<CardUser> _userManager;

    public ExportModel(BackupFactory backupFactory, UserManager<CardUser> userManager)
    {
        _backupFactory = backupFactory;
        _userManager = userManager;
    }

    [BindProperty]
    [Display(Name = "Backup Type")]
    public DataScope DataScope { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancel)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        string? userId = _userManager.GetUserId(User);

        if (userId is null)
        {
            return NotFound();
        }

        var backup = DataScope switch // file stream should close backup
        {
            DataScope.User => await _backupFactory.GetUserBackupAsync(userId, cancel),
            DataScope.Treasury => await _backupFactory.GetTreasuryBackupAsync(cancel),
            DataScope.Complete or _ => await _backupFactory.GetDefaultBackupAsync(cancel)
        };

        string userName = _userManager.GetDisplayName(User) ?? userId;
        string timestamp = DateTime.UtcNow.ToString(CultureInfo.InvariantCulture);

        string filename = DataScope switch
        {
            DataScope.User => $"cards-{timestamp}-{userName}.json",
            DataScope.Treasury => $"cards-{timestamp}-treasury.json",
            DataScope.Complete or _ => $"cards-{timestamp}-complete.json"
        };

        return File(backup, "application/json", filename);
    }
}
