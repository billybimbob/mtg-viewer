using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Services
{
    public class CardUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<CardUser>
    {
        public CardUserClaimsPrincipalFactory(
            UserManager<CardUser> userManager, IOptions<IdentityOptions> options)
            : base(userManager, options)
        { }

        public async override Task<ClaimsPrincipal> CreateAsync(CardUser user)
        {
            var principal = await base.CreateAsync(user);

            if (principal.Identity is ClaimsIdentity identity)
            {
                identity.AddClaim( new Claim(CardUser.DisplayNameClaim, user.DisplayName) );
            }

            return principal;
        }
    }
}


namespace Microsoft.AspNetCore.Identity
{
    public static class UserManagerExtensions
    {
        public static string? GetDisplayName(this UserManager<CardUser> _, ClaimsPrincipal user)
        {
            return user?.FindFirstValue(CardUser.DisplayNameClaim);
        }
    }
}