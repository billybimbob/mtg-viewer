using Microsoft.AspNetCore.Identity;

using System.ComponentModel.DataAnnotations;

namespace MTGViewer.Areas.Identity.Data;

// Add profile data for application users by adding properties to the CardUser class
public class CardUser : IdentityUser
{
    [PersonalData]
    [StringLength(256, MinimumLength = 1)]
    [Display(Name = "Name")]
    public string DisplayName { get; set; } = default!;

    public bool IsApproved { get; set; }
}
