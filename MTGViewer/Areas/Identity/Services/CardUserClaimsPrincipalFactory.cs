using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Services
{
    public class CardUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<CardUser>
    {
        private readonly ReferenceManager _referenceManager;

        public CardUserClaimsPrincipalFactory(
            UserManager<CardUser> userManager,
            ReferenceManager referenceManager,
            IOptions<IdentityOptions> options)
            : base(userManager, options)
        {
            _referenceManager = referenceManager;
        }

        protected async override Task<ClaimsIdentity> GenerateClaimsAsync(CardUser user)
        {
            var id = await base.GenerateClaimsAsync(user);

            var reference = await _referenceManager.References
                .SingleOrDefaultAsync(u => u.Id == user.Id);

            if (reference == default)
            {
                return id;
            }

            id.AddClaim( new Claim(CardClaims.DisplayName, reference.Name) );

            if (!reference.ResetRequested)
            {
                id.AddClaim( new Claim(CardClaims.ChangeTreasury, reference.Id) );
            }

            return id;
        }
    }
}


namespace Microsoft.AspNetCore.Identity
{
    public static class UserManagerExtensions
    {
        public static string? GetDisplayName(this UserManager<CardUser> _, ClaimsPrincipal user)
        {
            return user?.FindFirstValue(CardClaims.DisplayName);
        }
    }
}