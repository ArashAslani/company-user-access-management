using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CompanyAccessManagement.Infrastructure.Identity;

/// <summary>
/// Refuses inactive or deleted accounts on password login (<c>/login</c>) and when a refresh token is exchanged
/// (<c>/refresh</c> validates the security stamp), so both return 401.
/// </summary>
public class ApplicationSignInManager : SignInManager<ApplicationUser>
{
    public ApplicationSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
    }

    public static bool IsUsable(ApplicationUser user) => user.IsActive && !user.IsDeleted;

    public override async Task<bool> CanSignInAsync(ApplicationUser user)
        => IsUsable(user) && await base.CanSignInAsync(user);

    public override async Task<ApplicationUser?> ValidateSecurityStampAsync(ClaimsPrincipal? principal)
    {
        var user = await base.ValidateSecurityStampAsync(principal);
        return user is not null && IsUsable(user) ? user : null;
    }
}
