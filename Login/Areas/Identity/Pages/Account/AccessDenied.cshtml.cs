using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Login.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class AccessDeniedModel : DeniedPageModel { }

public abstract class DeniedPageModel : PageModel
{
    // Navigation comes from the principal, never from an untrusted ReturnUrl.
    public string HomePath => User.IsInRole("SuperAdmin") ? "/SuperAdmin/Dashboard"
        : User.IsInRole("Gestor") ? "/Operaciones"
        : User.IsInRole("Cliente") ? "/Tienda/Ordenes"
        : User.IsInRole("Piloto") ? "/Piloto/Index"
        : "/Identity/Account/Login";

    public bool SignedIn => User.Identity?.IsAuthenticated == true;

    public void OnGet()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.Headers.CacheControl = "no-store";
    }
}
