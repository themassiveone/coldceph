using ColdCeph.Control.Features.Auth.Services;
using ColdCeph.Core.Features.Auth.DTOs;

namespace ColdCeph.Control.Features.Auth.Controllers;

public sealed class AuthController
{
    private readonly AuthService _service;

    public AuthController(AuthService service)
    {
        _service = service;
    }

    public OperatorPrincipalDto GetCurrentPrincipal() => _service.GetCurrentPrincipal();
}
