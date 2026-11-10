using System.Security.Claims;
using Login.Application.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Login.Controllers.Api;

[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn,
    MobileSessionService sessions) : ControllerBase
{
    public sealed record LoginRequest(string Email, string Password);
    public sealed record RefreshRequest(string RefreshToken);
    public sealed record MeResponse(string UserId, string Email, IList<string> Roles);

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { error = "Email y password requeridos." });
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            return Unauthorized(new { error = "Credenciales inválidas." });
        return Ok(await sessions.CreateAsync(user));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
    {
        var response = await sessions.RefreshAsync(request.RefreshToken);
        return response is null ? Unauthorized(new { error = "La sesión venció. Inicia sesión nuevamente." }) : Ok(response);
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
    {
        await sessions.RevokeAsync(request.RefreshToken);
        return Ok(new { ok = true });
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Me()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = id is null ? null : await users.FindByIdAsync(id);
        if (user is null) return Unauthorized(new { error = "Usuario no encontrado." });
        return Ok(new MeResponse(user.Id, user.Email ?? "", await users.GetRolesAsync(user)));
    }
}
