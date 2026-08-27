using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Login.Controllers.Api
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly IConfiguration _config;

        public AuthController(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            IConfiguration config)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _config = config;
        }

        public sealed record LoginRequest(string Email, string Password);
        public sealed record LoginResponse(string Token, DateTime ExpiresAt, string Email, IList<string> Roles);
        public sealed record MeResponse(string UserId, string Email, IList<string> Roles);

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { error = "Email y password requeridos." });

            var user = await _userManager.FindByEmailAsync(request.Email.Trim());
            if (user is null)
                return Unauthorized(new { error = "Credenciales inválidas." });

            var check = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (!check.Succeeded)
                return Unauthorized(new { error = "Credenciales inválidas." });

            var roles = await _userManager.GetRolesAsync(user);

            var token = GenerarToken(user, roles);

            return Ok(new LoginResponse(token, DateTime.UtcNow.AddMinutes(ExpireMinutes()), user.Email!, roles));
        }

        [HttpGet("me")]
        [Authorize(AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> Me()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { error = "No autenticado." });

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
                return Unauthorized(new { error = "Usuario no encontrado." });

            var roles = await _userManager.GetRolesAsync(user);
            return Ok(new MeResponse(user.Id, user.Email ?? "", roles));
        }

        // ---------- Helpers ----------

        private int ExpireMinutes()
        {
            return int.TryParse(_config["Jwt:ExpireMinutes"], out var m) ? m : 1440;
        }

        private string GenerarToken(IdentityUser user, IList<string> roles)
        {
            var key = _config["Jwt:Key"] ?? "PilotGo_Dev_Only_Secret_Key_ChangeMe_1234567890";
            var issuer = _config["Jwt:Issuer"] ?? "PilotGo";
            var audience = _config["Jwt:Audience"] ?? "PilotGoMobile";

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id),
                new Claim(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName ?? user.Email ?? ""),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            foreach (var rol in roles)
                claims.Add(new Claim("role", rol));

            var keyBytes = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
            var creds = new SigningCredentials(keyBytes, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddMinutes(ExpireMinutes()),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
