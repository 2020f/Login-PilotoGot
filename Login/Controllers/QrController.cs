using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRCoder;
using Login.Data;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Login.Controllers
{
    [Authorize] // opcional (yo lo dejaría para que nadie genere QRs random)
    public class QrController : Controller
    {
        private readonly ApplicationDbContext _db;
        public QrController(ApplicationDbContext db) => _db = db;

        // El navegador pide una orden autorizada; el PIN no viaja en la URL.
        [HttpGet]
        [Authorize(Roles = "Cliente")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Orden(int ordenId, int? tiendaId = null)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var codigo = await _db.OrdenesEntrega.AsNoTracking()
                .Where(o => o.Id == ordenId && (tiendaId == null || o.TiendaId == tiendaId)
                    && o.Tienda.Activo && o.ClienteApp.Estado == EstadoClienteApp.Activo
                    && _db.UsuariosTienda.Any(u => u.TiendaId == o.TiendaId
                        && u.IdentityUserId == userId && u.Activo))
                .Select(o => o.Codigos.Where(c => c.Tipo == TipoCodigo.B_Recoleccion)
                    .Select(c => c.Codigo).FirstOrDefault()).SingleOrDefaultAsync();
            if (string.IsNullOrEmpty(codigo)) return NotFound();
            return Generar(codigo);
        }

        // GET: /Qr/Generar?codigo=ABC123
        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Generar(string codigo, int pixels = 12)
        {
            // Validación mínima anti-“troll”
            if (string.IsNullOrWhiteSpace(codigo))
                return BadRequest("Código requerido.");

            codigo = codigo.Trim();

            if (codigo.Length > 120) // evita payloads raros
                return BadRequest("Código demasiado largo.");

            // Generar QR
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(codigo, QRCodeGenerator.ECCLevel.Q);

            var qr = new PngByteQRCode(data);
            byte[] bytes = qr.GetGraphic(Math.Clamp(pixels, 1, 24));

            return File(bytes, "image/png");
        }
    }
}
