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
public sealed class ConfirmEmailModel(UserManager<IdentityUser> users) : PageModel
{
    public bool Confirmed { get; private set; }
    public async Task<IActionResult> OnGetAsync(string? userId = null, string? code = null)
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        if (string.IsNullOrEmpty(userId) || userId.Length > 450 || string.IsNullOrEmpty(code) || code.Length > 8192) return Page();
        string token;
        try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return Page(); }
        var user = await users.FindByIdAsync(userId);
        if (user is not null) Confirmed = (await users.ConfirmEmailAsync(user, token)).Succeeded;
        return Page();
    }
}
