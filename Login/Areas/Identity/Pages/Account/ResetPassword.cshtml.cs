using System.ComponentModel.DataAnnotations;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Login.Areas.Identity.Pages.Account;

[AllowAnonymous]
[EnableRateLimiting("AccountRecovery")]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class ResetPasswordModel(UserManager<IdentityUser> users) : PageModel
{
    public const string InvalidLinkMessage = "El enlace no es válido, ya fue utilizado o venció. Solicita un nuevo enlace de recuperación.";
    [BindProperty] public InputModel Input { get; set; } = new();
    public bool InvalidLink { get; private set; }
    public sealed class InputModel
    {
        [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
        [Required, StringLength(8192)] public string Code { get; set; } = "";
        [Required(ErrorMessage = "Escribe una contraseña.")]
        [StringLength(128, MinimumLength = 6, ErrorMessage = "La contraseña debe tener entre 6 y 128 caracteres.")]
        [DataType(DataType.Password)] public string Password { get; set; } = "";
        [Required, DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Las contraseñas no coinciden.")]
        public string ConfirmPassword { get; set; } = "";
    }

    public IActionResult OnGet(string? code = null, string? email = null)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (string.IsNullOrEmpty(code) || code.Length > 8192 || string.IsNullOrEmpty(email) || email.Length > 254)
            InvalidLink = true;
        else Input = new() { Code = code, Email = email };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!ModelState.IsValid) return Page();
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(Input.Code)); }
        catch (FormatException) { return Reject(); }
        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user is null) return Reject();
        var result = await users.ResetPasswordAsync(user, token, Input.Password);
        if (!result.Succeeded) return Reject();
        // El reset no activa comercios, membresías ni roles y no inicia sesión.
        return RedirectToPage("./ResetPasswordConfirmation");
    }

    private IActionResult Reject()
    {
        ModelState.AddModelError("", InvalidLinkMessage);
        return Page();
    }
}
