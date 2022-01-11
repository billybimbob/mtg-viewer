using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace MTGViewer.Areas.Identity.Data;

// Add profile data for application users by adding properties to the CardUser class
public class CardUser : IdentityUser
{
    public const string DisplayNameClaim = "CardUserDisplayName";

    [PersonalData]
    [MaxLength(256)]
    [Display(Name = "Name")]
    public string DisplayName { get; set; } = null!;

    public bool IsApproved { get; set; }
}