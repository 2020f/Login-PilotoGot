using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace Login.Controllers
{
    [Authorize] // opcional (yo lo dejaría para que nadie genere QRs random)
    public class QrController : Controller
    {
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
            byte[] bytes = qr.GetGraphic(pixels);

            return File(bytes, "image/png");
        }
    }
}
