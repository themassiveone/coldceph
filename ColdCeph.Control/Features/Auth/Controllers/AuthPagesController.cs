using ColdCeph.Control.Features.Auth.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ColdCeph.Control.Features.Auth.Controllers;

public sealed class AuthPagesController : Controller
{
    private readonly AuthService _auth;

    public AuthPagesController(AuthService auth)
    {
        _auth = auth;
    }

    [HttpGet("/auth/login")]
    [AllowAnonymous]
    public IActionResult Login() => View("Login");

    [HttpPost("/auth/login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginPost(string password)
    {
        if (!_auth.IsPasswordValid(password))
            return Unauthorized();
        await _auth.SignInAsync("operator");
        return Redirect("/");
    }

    [HttpPost("/auth/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _auth.SignOutAsync();
        return Redirect("/auth/login");
    }
}
