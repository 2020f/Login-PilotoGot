using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Login.Data;
using Login.Application.Services;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Login.ViewModels.SuperAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public partial class SuperAdminController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly QuotaPolicyService _quotas;
        private readonly SaasBillingService _billing;
        private readonly TimeProvider _clock;

        public SuperAdminController(ApplicationDbContext db, UserManager<IdentityUser> userManager, QuotaPolicyService quotas, SaasBillingService billing, TimeProvider clock)
        {
            _db = db;
            _userManager = userManager;
            _quotas = quotas;
            _billing = billing;
            _clock = clock;
        }

        // =========================
        // PLANES
        // =========================


        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var todayUtc = DateTime.SpecifyKind(_billing.Today.AddHours(4), DateTimeKind.Utc);
            var tomorrowUtc = todayUtc.AddDays(1);
            var vm = new DashboardVm
            {
                Empresas = await _db.ClientesApp.CountAsync(),
                EmpresasActivas = await _db.ClientesApp.CountAsync(c => c.Estado == EstadoClienteApp.Activo),
                EmpresasBloqueadas = await _db.ClientesApp.CountAsync(c => c.Estado != EstadoClienteApp.Activo),
                SinGestor = await _db.ClientesApp.CountAsync(c => c.GestorIdentityUserId == null),
                ComerciosActivos = await _db.Tiendas.CountAsync(t => t.Activo),
                PilotosActivos = await _db.Pilotos.CountAsync(p => p.Activo),
                OrdenesAbiertas = await _db.OrdenesEntrega.CountAsync(o => !EstadosEntrega.Terminales.Contains(o.Estado)),
                OrdenesHoy = await _db.OrdenesEntrega.CountAsync(o => o.CreatedAt >= todayUtc && o.CreatedAt < tomorrowUtc),
                EntregadasHoy = await _db.OrdenesEntrega.CountAsync(o => o.EntregadaAt >= todayUtc && o.EntregadaAt < tomorrowUtc),
                GeneradoAt = _clock.GetUtcNow().UtcDateTime,
                SolicitudesPendientes = await _db.SaasPlanRequests.CountAsync(r => r.Status == "Pendiente"),
                EmpresasPorVencer = await _db.ClientesApp.CountAsync(c => c.FechaFinPlan >= _billing.Today && c.FechaFinPlan < _billing.Today.AddDays(8))
            };
            var invoices = await _db.SaasInvoices.AsNoTracking().Include(i => i.Payments).ToListAsync();
            var companies = await _db.ClientesApp.AsNoTracking().Select(c => new { c.Id, c.Estado }).ToListAsync();
            var expired = await _billing.GetExpiredStatesAsync(companies.Select(c => c.Id));
            vm.EmpresasActivas = companies.Count(c => c.Estado == EstadoClienteApp.Activo && !expired[c.Id]);
            vm.EmpresasBloqueadas = companies.Count - vm.EmpresasActivas;
            vm.PendienteCobro = invoices.Where(i => i.VoidedAt == null).Sum(SaasBillingService.Balance);
            vm.FacturasVencidas = invoices.Count(i => SaasBillingService.Status(i, _billing.Today) == "Vencida");
            var month = new DateTime(_billing.Today.Year, _billing.Today.Month, 1);
            vm.CobradoMes = invoices.SelectMany(i => i.Payments).Where(p => p.VoidedAt == null && p.PaidOn >= month && p.PaidOn < month.AddMonths(1)).Sum(p => p.Amount);
            Response.Headers.CacheControl = "no-store";
            return View(vm);
        }

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
            if (decimal.Round(input.PrecioMensual, 2) != input.PrecioMensual) ModelState.AddModelError(nameof(input.PrecioMensual), "El precio admite hasta dos decimales.");
            if (string.IsNullOrWhiteSpace(input.Nombre)) ModelState.AddModelError(nameof(input.Nombre), "Escribe el nombre del plan.");
            if (!ModelState.IsValid)
                return View(input);

            await using var createPlanTx = await _db.Database.BeginTransactionAsync();
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
            Audit(null, "PlanCreado", "Plan", "", $"Plan {input.Nombre.Trim()} creado; precio mensual DOP {input.PrecioMensual}.");
            await _db.SaveChangesAsync();
            await createPlanTx.CommitAsync();
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
            if (decimal.Round(input.PrecioMensual, 2) != input.PrecioMensual) ModelState.AddModelError(nameof(input.PrecioMensual), "El precio admite hasta dos decimales.");
            if (string.IsNullOrWhiteSpace(input.Nombre)) ModelState.AddModelError(nameof(input.Nombre), "Escribe el nombre del plan.");
            if (!ModelState.IsValid) return View(input);

            var plan = await _db.Planes.SingleOrDefaultAsync(p => p.Id == input.Id);
            if (plan is null) return RedirectToAction(nameof(Planes), new { error = "Plan no existe." });

            if (await _db.ClientesApp.AnyAsync(c => c.PlanId == plan.Id) &&
                (Reduces(plan.MaxTiendas, input.MaxTiendas) || Reduces(plan.MaxPilotos, input.MaxPilotos) || Reduces(plan.MaxOrdenesPorMes, input.MaxOrdenesPorMes)))
            {
                ModelState.AddModelError("", "Este plan tiene empresas asignadas. Crea un plan nuevo para reducir límites y cambia cada empresa con la validación de su uso actual.");
                return View(input);
            }
            Audit(null, "PlanEditado", "Plan", plan.Id.ToString(), $"{plan.Nombre}: precio {plan.PrecioMensual} → {input.PrecioMensual}; límites {input.MaxTiendas}/{input.MaxPilotos}/{input.MaxOrdenesPorMes}. Las facturas emitidas conservan sus importes.");

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
            Audit(null, "DisponibilidadPlan", "Plan", plan.Id.ToString(), plan.Activo ? "Disponible para nuevas contrataciones." : "Retirado de nuevas contrataciones; conserva empresas actuales.");
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Planes), new { ok = $"Plan {(plan.Activo ? "activado" : "desactivado")} ✅" });
        }








        // =========================
        // CLIENTE APP (TENANTS)
        // =========================








        // Helper mínimo para auditoría (no cambia tu lógica)
        private string GetAuditUser()
        {
            var email = User?.Claims?.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
            if (!string.IsNullOrWhiteSpace(email)) return email;

            if (!string.IsNullOrWhiteSpace(User?.Identity?.Name)) return User.Identity!.Name!;

            return "system";
        }

        private static bool Reduces(int current, int next) => next > 0 && (current == 0 || next < current);

        private void Audit(int? tenant, string action, string entity, string id, string detail)
            => _db.SaasAuditLogs.Add(new Login.Domain.Saas.SaasAuditLog { ClienteAppId = tenant, Action = action,
                Entity = entity, EntityId = id, Detail = detail, Actor = GetAuditUser(), CreatedAt = _clock.GetUtcNow().UtcDateTime });

        [HttpGet]
        public async Task<IActionResult> Clientes(string? ok = null, string? error = null, string? q = null, int pagina = 1)
        {
            pagina = Math.Clamp(pagina, 1, 1000000);
            var query = _db.ClientesApp.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().ToLower();
                query = query.Where(c => c.NombreComercial.ToLower().Contains(term) || c.Plan.Nombre.ToLower().Contains(term)
                    || (c.Email != null && c.Email.ToLower().Contains(term))
                    || _db.Users.Any(u => u.Id == c.GestorIdentityUserId && u.Email != null && u.Email.ToLower().Contains(term)));
            }
            var total = await query.CountAsync();
            var clientes = await query.OrderByDescending(c => c.Id).Skip((pagina - 1) * 20).Take(20).Select(c => new ClienteAppRowVm
            {
                Id = c.Id, NombreComercial = c.NombreComercial, PlanNombre = c.Plan.Nombre, Activo = c.Estado == EstadoClienteApp.Activo,
                Estado = c.Estado.ToString(), GestorEmail = _db.Users.Where(u => u.Id == c.GestorIdentityUserId).Select(u => u.Email).FirstOrDefault(),
                MaxTiendas = c.Plan.MaxTiendas, MaxPilotos = c.MaxPilotos ?? c.Plan.MaxPilotos,
                TiendasCount = c.Tiendas.Count(t => t.Activo), PilotosCount = c.Pilotos.Count(p => p.Activo)
                , Email = c.Email, FechaFinPlan = c.FechaFinPlan, ProximoCobro = c.ProximoCobro, EsTrial = c.EsTrial
            }).ToListAsync();
            var expired = await _billing.GetExpiredStatesAsync(clientes.Select(c => c.Id));
            foreach (var row in clientes)
                if (row.Activo && expired[row.Id]) { row.Estado = "Vencido"; row.Activo = false; }
            ViewBag.Planes = await _db.Planes.AsNoTracking().Where(p => p.Activo).OrderBy(p => p.Nombre).ToListAsync();
            return View(new ClientesIndexVm { Ok = ok, Error = error, Clientes = clientes, Q = q, Total = total, Pagina = pagina });
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
            ValidateSubscriptionDates(input);
            if (string.IsNullOrWhiteSpace(input.NombreComercial)) ModelState.AddModelError(nameof(input.NombreComercial), "Escribe el nombre de la empresa.");
            if (input.UltimoPago.HasValue || input.ProximoCobro.HasValue) ModelState.AddModelError("", "Los pagos y próximos cobros se generan desde Facturación, al registrar operaciones reales.");
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

            await using var createTenantTx = await _db.Database.BeginTransactionAsync();
            var newTenant = new ClienteApp
            {
                NombreComercial = input.NombreComercial.Trim(),

                Ubicacion = string.IsNullOrWhiteSpace(input.Ubicacion) ? null : input.Ubicacion.Trim(),
                Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim(),

                PlanId = plan.Id,
                // GestorIdentityUserId queda null hasta crear gestor

                // =========================
                // NUEVOS CAMPOS (nullable)
                // =========================
                Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim(),
                RNC = string.IsNullOrWhiteSpace(input.RNC) ? null : input.RNC.Trim(),
                ContactoResponsable = string.IsNullOrWhiteSpace(input.ContactoResponsable) ? null : input.ContactoResponsable.Trim(),

                FechaInicioPlan = input.EsTrial ? (input.FechaInicioPlan ?? _billing.Today).Date : _billing.Today,
                FechaFinPlan = input.EsTrial ? input.FechaFinPlan?.Date : _billing.Today,

                UltimoPago = null,
                ProximoCobro = null,

                EsTrial = input.EsTrial,

                MaxUsuarios = input.MaxUsuarios,
                MaxOrdenesMes = input.MaxOrdenesMes,
                MaxPilotos = input.MaxPilotos,

                Observaciones = string.IsNullOrWhiteSpace(input.Observaciones) ? null : input.Observaciones.Trim(),

                // Auditoría (no cambia lógica, solo deja rastro)
                UpdatedAt = _clock.GetUtcNow().UtcDateTime,
                UpdatedBy = GetAuditUser()
            };
            _db.ClientesApp.Add(newTenant);

            await _db.SaveChangesAsync();
            Audit(newTenant.Id, "EmpresaCreada", "ClienteApp", newTenant.Id.ToString(), $"Empresa creada con plan {plan.Nombre}. Sin pago registrado.");
            await _db.SaveChangesAsync();
            await createTenantTx.CommitAsync();
            return RedirectToAction(nameof(Clientes), new { ok = "ClienteApp creado ✅ (falta crear su gestor)" });
        }

        [HttpGet]
        public async Task<IActionResult> EditarCliente(int id, string? error = null)
        {
            if (!string.IsNullOrWhiteSpace(error)) ModelState.AddModelError("", error);
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
                Estado = cliente.Estado,

                // =========================
                // NUEVOS CAMPOS
                // =========================
                Email = cliente.Email,
                RNC = cliente.RNC,
                ContactoResponsable = cliente.ContactoResponsable,

                FechaInicioPlan = cliente.FechaInicioPlan,
                FechaFinPlan = cliente.FechaFinPlan,

                UltimoPago = cliente.UltimoPago,
                ProximoCobro = cliente.ProximoCobro,

                EsTrial = cliente.EsTrial ?? false,

                MaxUsuarios = cliente.MaxUsuarios,
                MaxOrdenesMes = cliente.MaxOrdenesMes,
                MaxPilotos = cliente.MaxPilotos,

                Observaciones = cliente.Observaciones
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditarCliente(ClienteAppEditVm input)
        {
            if (string.IsNullOrWhiteSpace(input.NombreComercial)) ModelState.AddModelError(nameof(input.NombreComercial), "Escribe el nombre de la empresa.");
            if (!Enum.IsDefined(input.Estado)) ModelState.AddModelError(nameof(input.Estado), "Selecciona un estado válido.");
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

            if (input.PlanId != cliente.PlanId)
                return RedirectToAction(nameof(DetalleCliente), new { id = cliente.Id, error = "Cambia el plan desde Suscripción para validar límites y registrar el motivo." });

            await using var editTx = await _quotas.BeginTenantMutationAsync(cliente.Id, requireActive: false);
            await _db.Entry(cliente).ReloadAsync();
            if (input.PlanId != cliente.PlanId)
                return RedirectToAction(nameof(DetalleCliente), new { id = cliente.Id, error = "El plan cambió mientras editabas. Revisa la suscripción actual y vuelve a guardar." });

            var plan = await _db.Planes.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == cliente.PlanId);
            if (plan is null)
                return RedirectToAction(nameof(Clientes), new { error = "Plan inválido o inactivo." });

            cliente.NombreComercial = input.NombreComercial.Trim();
            cliente.Ubicacion = string.IsNullOrWhiteSpace(input.Ubicacion) ? null : input.Ubicacion.Trim();
            cliente.Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim();
            cliente.Estado = input.Estado;

            // =========================
            // NUEVOS CAMPOS (guardar)
            // =========================
            cliente.Email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
            cliente.RNC = string.IsNullOrWhiteSpace(input.RNC) ? null : input.RNC.Trim();
            cliente.ContactoResponsable = string.IsNullOrWhiteSpace(input.ContactoResponsable) ? null : input.ContactoResponsable.Trim();

            // Cobertura, prueba y fechas de pago se gestionan por el libro SaaS;
            // la edición del perfil no puede simular un pago ni ampliar su vigencia.

            cliente.MaxUsuarios = input.MaxUsuarios;
            cliente.MaxOrdenesMes = input.MaxOrdenesMes;
            cliente.MaxPilotos = input.MaxPilotos;

            cliente.Observaciones = string.IsNullOrWhiteSpace(input.Observaciones) ? null : input.Observaciones.Trim();

            // Auditoría automática
            cliente.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            cliente.UpdatedBy = GetAuditUser();

            var effectivePilots = input.MaxPilotos ?? plan.MaxPilotos;
            var effectiveOrders = input.MaxOrdenesMes ?? plan.MaxOrdenesPorMes;
            var monthStart = DateTime.SpecifyKind(new DateTime(_billing.Today.Year, _billing.Today.Month, 1).AddHours(4), DateTimeKind.Utc);
            if ((effectivePilots > 0 && await _db.Pilotos.CountAsync(p => p.ClienteAppId == cliente.Id && p.Activo) > effectivePilots)
                || (input.MaxUsuarios is > 0 && await _quotas.CountUsersAsync(cliente.Id) > input.MaxUsuarios)
                || (effectiveOrders > 0 && await _db.OrdenesEntrega.CountAsync(o => o.ClienteAppId == cliente.Id && o.CreatedAt >= monthStart && o.CreatedAt < monthStart.AddMonths(1)) > effectiveOrders))
                return RedirectToAction(nameof(EditarCliente), new { id = cliente.Id, error = "Los límites nuevos son menores que el uso actual. Ajusta el plan sin eliminar usuarios ni órdenes." });
            Audit(cliente.Id, "EmpresaEditada", "ClienteApp", cliente.Id.ToString(), $"Perfil actualizado. Estado {cliente.Estado}. Límites efectivos pilotos/órdenes/usuarios {effectivePilots}/{effectiveOrders}/{input.MaxUsuarios ?? 0}.");
            await _db.SaveChangesAsync();
            await editTx.CommitAsync();
            return RedirectToAction(nameof(Clientes), new { ok = "ClienteApp actualizado ✅" });
        }

        private void ValidateSubscriptionDates(ClienteAppCreateVm input)
        {
            if ((input.FechaInicioPlan.HasValue && input.FechaInicioPlan.Value.Year is < 2000 or > 2100)
                || (input.FechaFinPlan.HasValue && input.FechaFinPlan.Value.Year is < 2000 or > 2100))
            { ModelState.AddModelError("", "Las fechas de suscripción deben estar entre 2000 y 2100."); return; }
            if (!input.EsTrial && input.FechaFinPlan?.Date > _billing.Today)
                ModelState.AddModelError(nameof(input.FechaFinPlan), "La cobertura de pago se genera al confirmar una factura. Sólo un período de prueba explícito admite una fecha de fin inicial.");
            if (!input.EsTrial && input.FechaInicioPlan?.Date > _billing.Today)
                ModelState.AddModelError(nameof(input.FechaInicioPlan), "El inicio de una suscripción pagada se establece desde su factura; no se concede cobertura futura desde el alta.");
            if (input.EsTrial && input.FechaFinPlan?.Date > (input.FechaInicioPlan?.Date ?? _billing.Today).AddDays(90))
                ModelState.AddModelError(nameof(input.FechaFinPlan), "La prueba no puede superar 90 días; una suscripción pagada se renueva desde Facturación.");
            if (input.FechaFinPlan.HasValue && input.FechaFinPlan.Value.Date < (input.FechaInicioPlan?.Date ?? _billing.Today))
                ModelState.AddModelError(nameof(input.FechaFinPlan), "El fin debe ser igual o posterior al inicio.");
            if (input.EsTrial && !input.FechaFinPlan.HasValue)
                ModelState.AddModelError(nameof(input.FechaFinPlan), "Una prueba necesita fecha de vencimiento.");
        }














        //[HttpGet]
        //public async Task<IActionResult> Clientes(string? ok = null, string? error = null)
        //{
        //    // Listado con conteos + email del gestor si existe
        //    var clientes = await _db.ClientesApp.AsNoTracking()
        //        .Include(c => c.Plan)
        //        .Select(c => new ClienteAppRowVm
        //        {
        //            Id = c.Id,
        //            NombreComercial = c.NombreComercial,
        //            PlanNombre = c.Plan.Nombre,
        //            Activo = c.Plan.Activo, // si quieres puedes cambiarlo por otra lógica
        //            Estado = c.Estado.ToString(),
        //            GestorEmail = null, // se completa abajo
        //            MaxTiendas = c.Plan.MaxTiendas,
        //            MaxPilotos = c.Plan.MaxPilotos,

        //            TiendasCount = c.Tiendas.Count,
        //            PilotosCount = c.Pilotos.Count
        //        })
        //        .OrderByDescending(x => x.Id)
        //        .ToListAsync();

        //    // Resolver email del gestor (Identity está aparte)
        //    var gestorIds = await _db.ClientesApp.AsNoTracking()
        //        .Where(c => c.GestorIdentityUserId != null)
        //        .Select(c => new { c.Id, c.GestorIdentityUserId })
        //        .ToListAsync();

        //    foreach (var item in gestorIds)
        //    {
        //        var row = clientes.FirstOrDefault(x => x.Id == item.Id);
        //        if (row == null) continue;

        //        var u = await _userManager.FindByIdAsync(item.GestorIdentityUserId!);
        //        row.GestorEmail = u?.Email;
        //    }

        //    var vm = new ClientesIndexVm
        //    {
        //        Ok = ok,
        //        Error = error,
        //        Clientes = clientes
        //    };


        //    ViewBag.Planes = await _db.Planes.AsNoTracking()
        //     .Where(p => p.Activo)
        //     .OrderBy(p => p.Nombre)
        //    .ToListAsync();






        //    return View(vm);
        //}

        //[HttpGet]
        //public async Task<IActionResult> CrearCliente()
        //{
        //    // Para el dropdown de planes
        //    ViewBag.Planes = await _db.Planes.AsNoTracking()
        //        .Where(p => p.Activo)
        //        .OrderBy(p => p.Nombre)
        //        .ToListAsync();

        //    return View(new ClienteAppCreateVm());
        //}

        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> CrearCliente(ClienteAppCreateVm input)
        //{
        //    if (!ModelState.IsValid)
        //    {
        //        ViewBag.Planes = await _db.Planes.AsNoTracking()
        //            .Where(p => p.Activo)
        //            .OrderBy(p => p.Nombre)
        //            .ToListAsync();

        //        return View(input);
        //    }

        //    var plan = await _db.Planes.SingleOrDefaultAsync(p => p.Id == input.PlanId && p.Activo);
        //    if (plan is null)
        //        return RedirectToAction(nameof(Clientes), new { error = "Plan inválido o inactivo." });

        //    _db.ClientesApp.Add(new ClienteApp
        //    {
        //        NombreComercial = input.NombreComercial.Trim(),

        //        Ubicacion = string.IsNullOrWhiteSpace(input.Ubicacion) ? null : input.Ubicacion.Trim(),
        //        Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim(),

        //        PlanId = plan.Id
        //        // GestorIdentityUserId queda null hasta crear gestor
        //    });

        //    await _db.SaveChangesAsync();
        //    return RedirectToAction(nameof(Clientes), new { ok = "ClienteApp creado ✅ (falta crear su gestor)" });
        //}



        //[HttpGet]
        //public async Task<IActionResult> EditarCliente(int id)
        //{
        //    var cliente = await _db.ClientesApp.AsNoTracking()
        //        .SingleOrDefaultAsync(c => c.Id == id);

        //    if (cliente is null)
        //        return RedirectToAction(nameof(Clientes), new { error = "ClienteApp no existe." });




        //    ViewBag.Planes = await _db.Planes.AsNoTracking()
        //.Where(p => p.Activo)
        //.OrderBy(p => p.Nombre)
        //.ToListAsync();

        //    var vm = new ClienteAppEditVm
        //    {
        //        Id = cliente.Id,
        //        NombreComercial = cliente.NombreComercial,
        //        Ubicacion = cliente.Ubicacion,
        //        Telefono = cliente.Telefono,
        //        PlanId = cliente.PlanId,
        //        Estado = cliente.Estado
        //    };

        //    return View(vm);



        //}









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

        //    var plan = await _db.Planes.AsNoTracking()
        //        .SingleOrDefaultAsync(p => p.Id == input.PlanId && p.Activo);
        //    if (plan is null)
        //        return RedirectToAction(nameof(Clientes), new { error = "Plan inválido o inactivo." });

        //    cliente.NombreComercial = input.NombreComercial.Trim();
        //    cliente.Ubicacion = string.IsNullOrWhiteSpace(input.Ubicacion) ? null : input.Ubicacion.Trim();
        //    cliente.Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim();
        //    cliente.PlanId = input.PlanId;
        //    cliente.Estado = input.Estado;

        //    await _db.SaveChangesAsync();
        //    return RedirectToAction(nameof(Clientes), new { ok = "ClienteApp actualizado ✅" });
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

            if (!await _db.ClientesApp.AnyAsync(c => c.Id == input.ClienteAppId)) return NotFound();
            await using var tx = await _quotas.BeginTenantMutationAsync(input.ClienteAppId, requireActive: false);
            try { await _quotas.EnsureNewUserAsync(input.ClienteAppId); }
            catch (InvalidOperationException ex) { ModelState.AddModelError("", ex.Message); return View(input); }

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
            var roleResult = await _userManager.AddToRoleAsync(user, "Gestor");
            if (!roleResult.Succeeded) return RedirectToAction(nameof(Clientes), new { error = "No se pudo asignar el rol del gestor." });

            // ✅ amarrar gestor al tenant
            cliente.GestorIdentityUserId = user.Id;
            Audit(cliente.Id, "GestorCreado", "Usuario", user.Id, $"Gestor {email} vinculado sólo a esta empresa.");
            await _db.SaveChangesAsync();

            await tx.CommitAsync();
            return RedirectToAction(nameof(Clientes), new { ok = "Gestor creado y asignado ✅" });
        }
    }
}
