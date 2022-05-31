using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MtgViewer.Areas.Identity.Data;

namespace MtgViewer.Areas.Identity.Services
{
    public class CardUserClaimsPrincipalFactory : UserClaimsPrincipalFactory<CardUser>
    {
        private readonly OwnerManager _ownerManager;
        private readonly ILogger<CardUserClaimsPrincipalFactory> _logger;

        public CardUserClaimsPrincipalFactory(
            UserManager<CardUser> userManager,
            OwnerManager ownerManager,
            ILogger<CardUserClaimsPrincipalFactory> logger,
            IOptions<IdentityOptions> options)
            : base(userManager, options)
        {
            _ownerManager = ownerManager;
            _logger = logger;
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(CardUser user)
        {
            var id = await base.GenerateClaimsAsync(user);

            var owner = await _ownerManager.Owners
                .OrderBy(o => o.Id)
                .SingleOrDefaultAsync(o => o.Id == user.Id);

            if (owner == default)
            {
                // reference is missing, add a new reference

                await GenerateNewOwnerAsync(user, id);
                return id;
            }

            id.AddClaim(new Claim(CardClaims.DisplayName, owner.Name));

            if (!owner.ResetRequested)
            {
                id.AddClaim(new Claim(CardClaims.ChangeTreasury, owner.Id));
            }

            return id;
        }

        private async Task GenerateNewOwnerAsync(CardUser user, ClaimsIdentity id)
        {
            try
            {
                await _ownerManager.CreateAsync(user);

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
            => user?.FindFirstValue(CardClaims.DisplayName);
    }
}
