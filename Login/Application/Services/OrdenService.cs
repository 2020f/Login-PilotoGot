using System;
using System.Threading.Tasks;
using Login.Application.Interfaces;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Services
{
    public class OrdenService : IOrdenService
    {
        private readonly ApplicationDbContext _db;

        public OrdenService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<OrdenCreadaResult> CrearOrdenAsync(
            int clienteAppId,
            int tiendaId,
            int usuarioFinalId,
            string? notaPedido = null,
            string actorIdentityUserId = "System",
            string actorRol = "System")
        {
            if (clienteAppId <= 0) throw new ArgumentException("clienteAppId inválido.");
            if (tiendaId <= 0) throw new ArgumentException("tiendaId inválido.");
            if (usuarioFinalId <= 0) throw new ArgumentException("usuarioFinalId inválido.");

            // 1) Validar límite mensual por plan
            await ValidarLimitePorPlanAsync(clienteAppId, DateTime.UtcNow);

            // 2) Validar tienda pertenece al ClienteApp
            var tienda = await _db.Tiendas
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == tiendaId && t.ClienteAppId == clienteAppId);

            if (tienda is null)
                throw new InvalidOperationException("La tienda no existe o no pertenece a este ClienteApp.");

            // 3) Validar usuario final pertenece a esa tienda
            var uf = await _db.UsuariosFinales
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.Id == usuarioFinalId && u.TiendaId == tiendaId);

            if (uf is null)
                throw new InvalidOperationException("El usuario final no existe o no pertenece a esa tienda.");

            // 4) Transacción para A secuencial por tenant
            using var tx = await _db.Database.BeginTransactionAsync();

            var cliente = await _db.ClientesApp.SingleOrDefaultAsync(c => c.Id == clienteAppId);
            if (cliente is null) throw new InvalidOperationException("ClienteApp no existe.");

            cliente.OrdenSeq += 1;
            var numeroA = cliente.OrdenSeq;

            // 5) Crear orden
            var orden = new OrdenEntrega
            {
                ClienteAppId = clienteAppId,
                TiendaId = tiendaId,
                UsuarioFinalId = usuarioFinalId,
                NumeroOrdenA = numeroA,
                Estado = EstadoOrden.Creada,
                NotaPedido = string.IsNullOrWhiteSpace(notaPedido) ? null : notaPedido
            };

            _db.OrdenesEntrega.Add(orden);
            await _db.SaveChangesAsync();

            // 6) Crear B y C en tabla CodigoEntrega
            var codigoB = await GenerarCodigoUnicoAsync();
            var codigoC = await GenerarCodigoUnicoAsync();

            _db.CodigosEntrega.Add(new CodigoEntrega
            {
                OrdenEntregaId = orden.Id,
                Tipo = TipoCodigo.B_Recoleccion,
                Codigo = codigoB
            });

            _db.CodigosEntrega.Add(new CodigoEntrega
            {
                OrdenEntregaId = orden.Id,
                Tipo = TipoCodigo.C_Finalizacion,
                Codigo = codigoC
            });

            // 7) Historial
            RegistrarHistorial(
                orden.Id,
                TipoEventoOrden.Creada,
                actorIdentityUserId,
                actorRol,
                $"Orden creada. A={numeroA}. Códigos B y C generados.");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return new OrdenCreadaResult(orden.Id, numeroA, codigoB, codigoC);
        }

        public async Task AsignarPilotoAsync(string codigoB, int pilotoId, string actorIdentityUserId, string actorRol)
        {
            if (string.IsNullOrWhiteSpace(codigoB)) throw new ArgumentException("codigoB requerido.");
            if (pilotoId <= 0) throw new ArgumentException("pilotoId inválido.");

            // Buscar orden por el código B
            var codeB = await _db.CodigosEntrega
                .Include(c => c.OrdenEntrega)
                .SingleOrDefaultAsync(c => c.Codigo == codigoB && c.Tipo == TipoCodigo.B_Recoleccion);

            if (codeB?.OrdenEntrega is null)
                throw new InvalidOperationException("Código B inválido.");

            var orden = codeB.OrdenEntrega;

            // Validar piloto pertenece al mismo ClienteApp
            var piloto = await _db.Pilotos.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == pilotoId && p.ClienteAppId == orden.ClienteAppId);

            if (piloto is null)
                throw new InvalidOperationException("El piloto no existe o no pertenece al mismo ClienteApp.");

            if (orden.Estado != EstadoOrden.Creada)
                throw new InvalidOperationException("Solo se puede asignar si la orden está en estado Creada.");

            orden.PilotoId = pilotoId;
            orden.AssignedAt = DateTime.UtcNow;
            orden.Estado = EstadoOrden.Asignada;

            RegistrarHistorial(
                orden.Id,
                TipoEventoOrden.Asignada,
                actorIdentityUserId,
                actorRol,
                $"Orden asignada a pilotoId={pilotoId}.");

            await _db.SaveChangesAsync();
        }

        public async Task ConfirmarRecolectaAsync(string codigoB, string actorIdentityUserId, string actorRol)
        {
            if (string.IsNullOrWhiteSpace(codigoB)) throw new ArgumentException("codigoB requerido.");

            var codeB = await _db.CodigosEntrega
                .Include(c => c.OrdenEntrega)
                .SingleOrDefaultAsync(c => c.Codigo == codigoB && c.Tipo == TipoCodigo.B_Recoleccion);

            if (codeB?.OrdenEntrega is null)
                throw new InvalidOperationException("Código B inválido.");

            if (codeB.Usado)
                throw new InvalidOperationException("Este código B ya fue usado.");

            var orden = codeB.OrdenEntrega;

            if (orden.PilotoId is null)
                throw new InvalidOperationException("No se puede recolectar: la orden no tiene piloto asignado.");

            if (orden.Estado != EstadoOrden.Asignada)
                throw new InvalidOperationException("La orden no está en estado válido para recolecta.");

            // Marcar B como usado
            codeB.Usado = true;
            codeB.UsadoAt = DateTime.UtcNow;

            // Cambiar estado
            orden.RecolectadaAt = DateTime.UtcNow;
            orden.Estado = EstadoOrden.Recolectada;

            RegistrarHistorial(
                orden.Id,
                TipoEventoOrden.Recolectada,
                actorIdentityUserId,
                actorRol,
                "Recolecta confirmada con código B.");

            await _db.SaveChangesAsync();
        }

        public async Task ConfirmarEntregaAsync(string codigoB, string codigoC, string actorIdentityUserId, string actorRol)
        {
            if (string.IsNullOrWhiteSpace(codigoB)) throw new ArgumentException("codigoB requerido.");
            if (string.IsNullOrWhiteSpace(codigoC)) throw new ArgumentException("codigoC requerido.");

            // Buscar orden por B
            var codeB = await _db.CodigosEntrega
                .Include(c => c.OrdenEntrega)
                .SingleOrDefaultAsync(c => c.Codigo == codigoB && c.Tipo == TipoCodigo.B_Recoleccion);

            if (codeB?.OrdenEntrega is null)
                throw new InvalidOperationException("Código B inválido.");

            var orden = codeB.OrdenEntrega;

            // Buscar C de la misma orden
            var codeC = await _db.CodigosEntrega
                .SingleOrDefaultAsync(c =>
                    c.OrdenEntregaId == orden.Id &&
                    c.Codigo == codigoC &&
                    c.Tipo == TipoCodigo.C_Finalizacion);

            if (codeC is null)
                throw new InvalidOperationException("Código C inválido para esta orden.");

            if (codeC.Usado)
                throw new InvalidOperationException("Este código C ya fue usado.");

            if (orden.Estado != EstadoOrden.Recolectada)
                throw new InvalidOperationException("La orden no está en estado válido para entrega.");

            // Marcar C usado
            codeC.Usado = true;
            codeC.UsadoAt = DateTime.UtcNow;

            // Cambiar estado
            orden.EntregadaAt = DateTime.UtcNow;
            orden.Estado = EstadoOrden.Entregada;

            RegistrarHistorial(
                orden.Id,
                TipoEventoOrden.Entregada,
                actorIdentityUserId,
                actorRol,
                "Entrega confirmada con códigos B + C.");

            await _db.SaveChangesAsync();
        }

        public async Task ValidarLimitePorPlanAsync(int clienteAppId, DateTime fechaUtc)
        {
            var inicioMes = new DateTime(fechaUtc.Year, fechaUtc.Month, 1);
            var finMes = inicioMes.AddMonths(1);

            var cliente = await _db.ClientesApp
                .Include(c => c.Plan)
                .SingleOrDefaultAsync(c => c.Id == clienteAppId);

            if (cliente is null) throw new InvalidOperationException("ClienteApp no existe.");
            if (cliente.Plan is null) throw new InvalidOperationException("ClienteApp no tiene plan asignado.");

            var limite = cliente.Plan.MaxOrdenesPorMes;

            var usadas = await _db.OrdenesEntrega.CountAsync(o =>
                o.ClienteAppId == clienteAppId &&
                o.CreatedAt >= inicioMes &&
                o.CreatedAt < finMes);

            if (usadas >= limite)
                throw new InvalidOperationException("Límite mensual de órdenes alcanzado según el plan.");
        }

        public async Task<OrdenPilotoView> ObtenerOrdenParaPilotoAsync(string codigoB, int pilotoId)
        {
            if (string.IsNullOrWhiteSpace(codigoB)) throw new ArgumentException("codigoB requerido.");
            if (pilotoId <= 0) throw new ArgumentException("pilotoId inválido.");

            var codeB = await _db.CodigosEntrega
                .Include(c => c.OrdenEntrega)
                    .ThenInclude(o => o.UsuarioFinal)
                .SingleOrDefaultAsync(c => c.Codigo == codigoB && c.Tipo == TipoCodigo.B_Recoleccion);

            if (codeB?.OrdenEntrega is null)
                throw new InvalidOperationException("Código B inválido.");

            var orden = codeB.OrdenEntrega;

            if (orden.PilotoId != pilotoId)
                throw new InvalidOperationException("Esta orden no está asignada a ese piloto.");

            // 🔒 Regla: no ve destino hasta recolectar
            var puedeVer = orden.Estado == EstadoOrden.Recolectada || orden.Estado == EstadoOrden.Entregada;

            return new OrdenPilotoView(
                OrdenId: orden.Id,
                NumeroOrdenA: orden.NumeroOrdenA,
                Estado: orden.Estado.ToString(),
                PuedeVerDestino: puedeVer,
                DestinoDireccion: puedeVer ? orden.UsuarioFinal.DireccionUbicacion : null,
                NombreUsuarioFinal: puedeVer ? orden.UsuarioFinal.Nombre : null,
                TelefonoUsuarioFinal: puedeVer ? orden.UsuarioFinal.Telefono : null
            );
        }

        // -------------------------
        // Helpers
        // -------------------------

        private async Task<string> GenerarCodigoUnicoAsync()
        {
            // 10 chars alfanuméricos
            while (true)
            {
                var code = Guid.NewGuid().ToString("N").ToUpperInvariant()[..10];

                var existe = await _db.CodigosEntrega.AnyAsync(c => c.Codigo == code);
                if (!existe) return code;
            }
        }

        private void RegistrarHistorial(
            int ordenEntregaId,
            TipoEventoOrden tipo,
            string actorIdentityUserId,
            string actorRol,
            string descripcion)
        {
            _db.HistorialOrdenes.Add(new HistorialOrden
            {
                OrdenEntregaId = ordenEntregaId,
                TipoEvento = tipo,
                ActorIdentityUserId = actorIdentityUserId,
                ActorRol = string.IsNullOrWhiteSpace(actorRol) ? "System" : actorRol,
                Descripcion = descripcion
            });
        }
    }
}
