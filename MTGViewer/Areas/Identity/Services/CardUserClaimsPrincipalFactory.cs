using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Areas.Identity.Services
{
    public class CardUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<CardUser>
    {
        private readonly ReferenceManager _referenceManager;
        private readonly ILogger<CardUserClaimsPrincipalFactory> _logger;

        public CardUserClaimsPrincipalFactory(
            UserManager<CardUser> userManager,
            ReferenceManager referenceManager,
            ILogger<CardUserClaimsPrincipalFactory> logger,
            IOptions<IdentityOptions> options)
            : base(userManager, options)
        {
            _referenceManager = referenceManager;
            _logger = logger;
        }

        protected async override Task<ClaimsIdentity> GenerateClaimsAsync(CardUser user)
        {
            var id = await base.GenerateClaimsAsync(user);

            var reference = await _referenceManager.References
                .OrderBy(u => u.Id)
                .SingleOrDefaultAsync(u => u.Id == user.Id);

            if (reference == default)
            {
                // reference is missing, add a new reference

                await GenerateNewReferenceAsync(user, id);
                return id;
            }

            id.AddClaim(new Claim(CardClaims.DisplayName, reference.Name));

            if (!reference.ResetRequested)
            {
                id.AddClaim(new Claim(CardClaims.ChangeTreasury, reference.Id));
            }

            return id;
        }

        private async Task GenerateNewReferenceAsync(CardUser user, ClaimsIdentity id)
        {
            try
            {
                await _referenceManager.CreateReferenceAsync(user);

                id.AddClaim(new Claim(CardClaims.DisplayName, user.DisplayName));
                id.AddClaim(new Claim(CardClaims.ChangeTreasury, user.Id));
            }
            catch (DbUpdateException e)
            {
                _logger.LogError("{Error}", e);
            }
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
