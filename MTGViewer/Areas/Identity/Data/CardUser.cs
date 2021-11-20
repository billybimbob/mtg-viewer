using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

using MTGViewer.Data;


namespace MTGViewer.Areas.Identity.Data
{
    // Add profile data for application users by adding properties to the CardUser class
    public class CardUser : IdentityUser
    {
        [PersonalData]
        public string? Name { get; set; }
    }
}
