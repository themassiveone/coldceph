using ColdCeph.Control.Composition;
using ColdCeph.Core.Features.Auth.DTOs;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;

namespace ColdCeph.Control.Features.Auth.Services;

public sealed class AuthService
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;
    private readonly ControlConfig _config;
    private readonly IHttpContextAccessor _http;

    public AuthService(ControlConfig config, IHttpContextAccessor http)
    {
        _config = config;
        _http = http;
    }

    public OperatorPrincipalDto GetCurrentPrincipal()
    {
        var user = _http.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
            return new OperatorPrincipalDto(user.Identity.Name ?? "operator", true);
        return new OperatorPrincipalDto("anonymous", false);
    }

    public bool IsPasswordValid(string password)
        => password == _config.OperatorPassword;

    public async Task SignInAsync(string name)
    {
        var context = _http.HttpContext ?? throw new InvalidOperationException("No HTTP context.");
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, name)],
            Scheme);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity));
    }

    public async Task SignOutAsync()
    {
        var context = _http.HttpContext ?? throw new InvalidOperationException("No HTTP context.");
        await context.SignOutAsync(Scheme);
    }
}
