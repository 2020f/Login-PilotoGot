using System.Diagnostics;
using Login.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[Authorize(Roles = "Admin")]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var requestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        var failedRequest = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        Response.StatusCode = StatusCodes.Status500InternalServerError;
        if (failedRequest?.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) == true)
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "No se pudo completar la solicitud. Inténtalo de nuevo.", traceId = requestId });
        return View("/Views/Shared/Error.cshtml", new ErrorViewModel { RequestId = requestId });
    }
}
