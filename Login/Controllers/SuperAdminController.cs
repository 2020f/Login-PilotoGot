using System.Linq;
using System.Threading.Tasks;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.ViewModels.SuperAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SuperAdminController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;

        public SuperAdminController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // =========================
        // PLANES
        // =========================

        [HttpGet]
        public async Task<IActionResult> Planes(string? ok = null, string? error = null)
        {
            var vm = new PlanesIndexVm
            {
                Ok = ok,
                Error = error,
                Planes = await _db.Planes.AsNoTracking()
                    .OrderByDescending(p => p.Id)
                    .ToListAsync()
            };

            return View(vm);
        }

        [HttpGet]
        public IActionResult CrearPlan()
            => View(new PlanCreateVm());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearPlan(PlanCreateVm input)
        {
            if (!ModelState.IsValid)
                return View(input);

            _db.Planes.Add(new Plan
            {
                Nombre = input.Nombre.Trim(),
                PrecioMensual = input.PrecioMensual,
                MaxTiendas = input.MaxTiendas,
                MaxPilotos = input.MaxPilotos,
                MaxOrdenesPorMes = input.MaxOrdenesPorMes,
                Activo = true
            });

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Planes), new { ok = "Plan creado ✅" });
        }


        [HttpGet]
        public async Task<IActionResult> EditarPlan(int id)
        {
            var plan = await _db.Planes.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id);
            if (plan is null) return RedirectToAction(nameof(Planes), new { error = "Plan no existe." });

            var vm = new PlanEditVm
            {
                Id = plan.Id,
                Nombre = plan.Nombre,
                PrecioMensual = plan.PrecioMensual,
                MaxTiendas = plan.MaxTiendas,
                MaxPilotos = plan.MaxPilotos,
                MaxOrdenesPorMes = plan.MaxOrdenesPorMes,
                Activo = plan.Activo
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarPlan(PlanEditVm input)
        {
            if (!ModelState.IsValid) return View(input);

            var plan = await _db.Planes.SingleOrDefaultAsync(p => p.Id == input.Id);
            if (plan is null) return RedirectToAction(nameof(Planes), new { error = "Plan no existe." });

            plan.Nombre = input.Nombre.Trim();
            plan.PrecioMensual = input.PrecioMensual;
            plan.MaxTiendas = input.MaxTiendas;
            plan.MaxPilotos = input.MaxPilotos;
            plan.MaxOrdenesPorMes = input.MaxOrdenesPorMes;
            plan.Activo = input.Activo;

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Planes), new { ok = "Plan actualizado ✅" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePlan(int id)
        {
            var plan = await _db.Planes.SingleOrDefaultAsync(p => p.Id == id);
            if (plan is null) return RedirectToAction(nameof(Planes), new { error = "Plan no existe." });

            plan.Activo = !plan.Activo;
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Planes), new { ok = $"Plan {(plan.Activo ? "activado" : "desactivado")} ✅" });
        }















        // =========================
        // CLIENTE APP (TENANTS)
        // =========================

        [HttpGet]
        public async Task<IActionResult> Clientes(string? ok = null, string? error = null)
        {
            // Listado con conteos + email del gestor si existe
            var clientes = await _db.ClientesApp.AsNoTracking()
                .Include(c => c.Plan)
                .Select(c => new ClienteAppRowVm
                {
                    Id = c.Id,
                    NombreComercial = c.NombreComercial,
                    PlanNombre = c.Plan.Nombre,
                    Activo = c.Plan.Activo, // si quieres puedes cambiarlo por otra lógica
                    Estado = c.Estado.ToString(),
                    GestorEmail = null, // se completa abajo
                    MaxTiendas = c.Plan.MaxTiendas,
                    MaxPilotos = c.Plan.MaxPilotos,

                    TiendasCount = c.Tiendas.Count,
                    PilotosCount = c.Pilotos.Count
                })
                .OrderByDescending(x => x.Id)
                .ToListAsync();

            // Resolver email del gestor (Identity está aparte)
            var gestorIds = await _db.ClientesApp.AsNoTracking()
                .Where(c => c.GestorIdentityUserId != null)
                .Select(c => new { c.Id, c.GestorIdentityUserId })
                .ToListAsync();

            foreach (var item in gestorIds)
            {
                var row = clientes.FirstOrDefault(x => x.Id == item.Id);
                if (row == null) continue;

                var u = await _userManager.FindByIdAsync(item.GestorIdentityUserId!);
                row.GestorEmail = u?.Email;
            }

            var vm = new ClientesIndexVm
            {
                Ok = ok,
                Error = error,
                Clientes = clientes
            };


            ViewBag.Planes = await _db.Planes.AsNoTracking()
             .Where(p => p.Activo)
             .OrderBy(p => p.Nombre)
            .ToListAsync();






            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> CrearCliente()
        {
            // Para el dropdown de planes
            ViewBag.Planes = await _db.Planes.AsNoTracking()
                .Where(p => p.Activo)
                .OrderBy(p => p.Nombre)
                .ToListAsync();

            return View(new ClienteAppCreateVm());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearCliente(ClienteAppCreateVm input)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Planes = await _db.Planes.AsNoTracking()
                    .Where(p => p.Activo)
                    .OrderBy(p => p.Nombre)
                    .ToListAsync();

                return View(input);
            }

            var plan = await _db.Planes.SingleOrDefaultAsync(p => p.Id == input.PlanId && p.Activo);
            if (plan is null)
                return RedirectToAction(nameof(Clientes), new { error = "Plan inválido o inactivo." });

            _db.ClientesApp.Add(new ClienteApp
            {
                NombreComercial = input.NombreComercial.Trim(),

                Ubicacion = string.IsNullOrWhiteSpace(input.Ubicacion) ? null : input.Ubicacion.Trim(),
                Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim(),

                PlanId = plan.Id
                // GestorIdentityUserId queda null hasta crear gestor
            });

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Clientes), new { ok = "ClienteApp creado ✅ (falta crear su gestor)" });
        }



        [HttpGet]
        public async Task<IActionResult> EditarCliente(int id)
        {
            var cliente = await _db.ClientesApp.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == id);

            if (cliente is null)
                return RedirectToAction(nameof(Clientes), new { error = "ClienteApp no existe." });




            ViewBag.Planes = await _db.Planes.AsNoTracking()
        .Where(p => p.Activo)
        .OrderBy(p => p.Nombre)
        .ToListAsync();

            var vm = new ClienteAppEditVm
            {
                Id = cliente.Id,
                NombreComercial = cliente.NombreComercial,
                Ubicacion = cliente.Ubicacion,
                Telefono = cliente.Telefono,
                PlanId = cliente.PlanId,
                Estado = cliente.Estado
            };

            return View(vm);








            //ViewBag.Planes = await _db.Planes.AsNoTracking()
            //    .Where(p => p.Activo)
            //    .OrderBy(p => p.Nombre)
            //    .ToListAsync();

            //var vm = new ClienteAppEditVm
            //{
            //    Id = cliente.Id,
            //    NombreComercial = cliente.NombreComercial,
            //    PlanId = cliente.PlanId,
            //    Estado = cliente.Estado
            //};

            //return View(vm);
        }









        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarCliente(ClienteAppEditVm input)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Planes = await _db.Planes.AsNoTracking()
                    .Where(p => p.Activo)
                    .OrderBy(p => p.Nombre)
                    .ToListAsync();

                return View(input);
            }

            var cliente = await _db.ClientesApp.SingleOrDefaultAsync(c => c.Id == input.Id);
            if (cliente is null)
                return RedirectToAction(nameof(Clientes), new { error = "ClienteApp no existe." });

            var plan = await _db.Planes.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == input.PlanId && p.Activo);
            if (plan is null)
                return RedirectToAction(nameof(Clientes), new { error = "Plan inválido o inactivo." });

            cliente.NombreComercial = input.NombreComercial.Trim();
            cliente.Ubicacion = string.IsNullOrWhiteSpace(input.Ubicacion) ? null : input.Ubicacion.Trim();
            cliente.Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim();
            cliente.PlanId = input.PlanId;
            cliente.Estado = input.Estado;

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Clientes), new { ok = "ClienteApp actualizado ✅" });
        }


























        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> EditarCliente(ClienteAppEditVm input)
        //{
        //    if (!ModelState.IsValid)
        //    {
        //        ViewBag.Planes = await _db.Planes.AsNoTracking()
        //            .Where(p => p.Activo)
        //            .OrderBy(p => p.Nombre)
        //            .ToListAsync();

        //        return View(input);
        //    }

        //    var cliente = await _db.ClientesApp.SingleOrDefaultAsync(c => c.Id == input.Id);
        //    if (cliente is null)
        //        return RedirectToAction(nameof(Clientes), new { error = "ClienteApp no existe." });

        //    var plan = await _db.Planes.AsNoTracking().SingleOrDefaultAsync(p => p.Id == input.PlanId && p.Activo);
        //    if (plan is null)
        //        return RedirectToAction(nameof(Clientes), new { error = "Plan inválido o inactivo." });

        //    cliente.NombreComercial = input.NombreComercial.Trim();
        //    cliente.PlanId = input.PlanId;
        //    cliente.Estado = input.Estado;

        //    await _db.SaveChangesAsync();
        //    return RedirectToAction(nameof(Clientes), new { ok = "ClienteApp actualizado ✅" });
        //}

        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> CambiarEstadoCliente(int id, EstadoClienteApp estado)
        //{
        //    var cliente = await _db.ClientesApp.SingleOrDefaultAsync(c => c.Id == id);
        //    if (cliente is null)
        //        return RedirectToAction(nameof(Clientes), new { error = "ClienteApp no existe." });

        //    cliente.Estado = estado;
        //    await _db.SaveChangesAsync();

        //    return RedirectToAction(nameof(Clientes), new { ok = $"Estado actualizado a {estado} ✅" });
        //}












        // =========================
        // CREAR GESTOR (1 por ClienteApp)
        // =========================

        [HttpGet]
        public async Task<IActionResult> CrearGestor(int clienteAppId)
        {
            var cliente = await _db.ClientesApp.AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == clienteAppId);

            if (cliente is null)
                return RedirectToAction(nameof(Clientes), new { error = "ClienteApp no existe." });

            if (!string.IsNullOrWhiteSpace(cliente.GestorIdentityUserId))
                return RedirectToAction(nameof(Clientes), new { error = "Este ClienteApp ya tiene gestor." });

            return View(new GestorCreateVm { ClienteAppId = clienteAppId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CrearGestor(GestorCreateVm input)
        {
            if (!ModelState.IsValid)
                return View(input);

            var cliente = await _db.ClientesApp.SingleOrDefaultAsync(c => c.Id == input.ClienteAppId);
            if (cliente is null)
                return RedirectToAction(nameof(Clientes), new { error = "ClienteApp no existe." });

            if (!string.IsNullOrWhiteSpace(cliente.GestorIdentityUserId))
                return RedirectToAction(nameof(Clientes), new { error = "Este ClienteApp ya tiene gestor." });

            var email = input.Email.Trim().ToLower();

            // no duplicados
            var existing = await _userManager.FindByEmailAsync(email);
            if (existing != null)
                return RedirectToAction(nameof(Clientes), new { error = "Ya existe un usuario con ese email." });

            var user = new IdentityUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var created = await _userManager.CreateAsync(user, input.Password);
            if (!created.Succeeded)
            {
                var msg = string.Join(" | ", created.Errors.Select(e => e.Description));
                return RedirectToAction(nameof(Clientes), new { error = msg });
            }

            // Rol Gestor
            await _userManager.AddToRoleAsync(user, "Gestor");

            // ✅ amarrar gestor al tenant
            cliente.GestorIdentityUserId = user.Id;
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Clientes), new { ok = "Gestor creado y asignado ✅" });
        }
    }
}
