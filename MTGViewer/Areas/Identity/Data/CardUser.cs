using Microsoft.AspNetCore.Identity;

namespace MTGViewer.Areas.Identity.Data;

// Add profile data for application users by adding properties to the CardUser class
public class CardUser : IdentityUser
{
    [PersonalData]
    public string Name { get; set; } = string.Empty;

    public bool IsApproved { get; set; }
}