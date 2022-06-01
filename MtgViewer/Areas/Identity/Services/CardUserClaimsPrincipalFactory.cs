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
        private readonly PlayerManager _playerManager;
        private readonly ILogger<CardUserClaimsPrincipalFactory> _logger;

        public CardUserClaimsPrincipalFactory(
            UserManager<CardUser> userManager,
            PlayerManager playerManager,
            ILogger<CardUserClaimsPrincipalFactory> logger,
            IOptions<IdentityOptions> options)
            : base(userManager, options)
        {
            _playerManager = playerManager;
            _logger = logger;
        }

        protected override async Task<ClaimsIdentity> GenerateClaimsAsync(CardUser user)
        {
            var id = await base.GenerateClaimsAsync(user);

            var player = await _playerManager.Players
                .OrderBy(p => p.Id)
                .SingleOrDefaultAsync(p => p.Id == user.Id);

            if (player == default)
            {
                // reference is missing, add a new reference

                await GenerateNewPlayerAsync(user, id);
                return id;
            }

            id.AddClaim(new Claim(CardClaims.DisplayName, player.Name));

            if (!player.ResetRequested)
            {
                id.AddClaim(new Claim(CardClaims.ChangeTreasury, player.Id));
            }

            return id;
        }

        private async Task GenerateNewPlayerAsync(CardUser user, ClaimsIdentity id)
        {
            try
            {
                await _playerManager.CreateAsync(user);

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
