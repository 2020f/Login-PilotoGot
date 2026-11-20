using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using Login.Services;
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
public sealed class ForgotPasswordModel(UserManager<IdentityUser> users, IAccountEmailSender sender) : PageModel
{
    public const string UnavailableMessage = "La recuperación por correo no está disponible en este momento. Contacta al administrador para recuperar tu acceso de forma segura.";
    public bool DeliveryAvailable => sender.IsConfigured && sender.PublicBaseUri is not null;
    [BindProperty] public InputModel Input { get; set; } = new();
    public sealed class InputModel
    {
        [Required(ErrorMessage = "Escribe tu correo.")]
        [EmailAddress(ErrorMessage = "Escribe un correo válido.")]
        [StringLength(254)]
        public string Email { get; set; } = "";
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();
        // La configuración es global: misma respuesta para correos existentes o ajenos.
        if (!DeliveryAvailable)
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ModelState.AddModelError("", UnavailableMessage);
            return Page();
        }
        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user is not null && !string.IsNullOrWhiteSpace(user.Email))
        {
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GeneratePasswordResetTokenAsync(user)));
            var path = Url.Page("./ResetPassword", values: new { code, email = user.Email })!;
            var link = new Uri(sender.PublicBaseUri!, path).AbsoluteUri;
            try
            {
                await sender.SendEmailAsync(user.Email, "Recuperar contraseña de Envirapido",
                    $"<p>Solicitaste recuperar tu acceso a Envirapido.</p><p><a href=\"{HtmlEncoder.Default.Encode(link)}\">Crear una nueva contraseña</a></p><p>El enlace vence en una hora y sólo permite un cambio. Si no lo solicitaste, ignora este mensaje.</p>");
            }
            catch (AccountEmailDeliveryException)
            {
                // Resultado genérico también ante fallo de transporte: no revela
                // si la dirección está registrada ni promete entrega de correo.
            }
        }
        return RedirectToPage("./ForgotPasswordConfirmation");
    }
}
