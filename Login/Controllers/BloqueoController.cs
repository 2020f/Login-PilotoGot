using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Login.Controllers
{
    [Authorize] // solo logueados llegan aquí (por el filtro)
    public class BloqueoController : Controller
    {
        [HttpGet]
        public IActionResult Index(string? motivo = null)
        {
            ViewBag.Motivo = motivo ?? "Suspendido";
            return View();
        }
    }
}
