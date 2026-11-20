using System;
using System.Threading.Tasks;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Login.Application.Exceptions;
using Login.Application.Interfaces;
using Login.Application.Operations;
using Login.Data;
using Login.Domain.Entities;
using Login.Domain.Enums;
using Login.Domain.Operations;
using Microsoft.EntityFrameworkCore;

namespace Login.Application.Services
{
    public partial class OrdenService : IOrdenService
    {
        private readonly ApplicationDbContext _db;
        private readonly IGoogleMapsService _googleMaps;
        private readonly QuotaPolicyService _quotas;
        private readonly TimeProvider _clock;

        public OrdenService(ApplicationDbContext db, IGoogleMapsService googleMaps, QuotaPolicyService quotas, TimeProvider? clock = null)
        {
            _db = db;
            _googleMaps = googleMaps;
            _quotas = quotas;
            _clock = clock ?? TimeProvider.System;
        }

        public async Task<OrdenCreadaResult> CrearOrdenAsync(
            int clienteAppId,
            int tiendaId,
            int usuarioFinalId,
            string? notaPedido = null,
            string actorIdentityUserId = "System",
            string actorRol = "System",
            string? operacionId = null,
            IReadOnlyList<OrdenDestinatarioRequest>? destinatarios = null)
        {
            var destinos = NormalizarDestinatarios(usuarioFinalId, destinatarios);
            usuarioFinalId = destinos[0].UsuarioFinalId;
            if (clienteAppId <= 0) throw new ArgumentException("clienteAppId inválido.");
            if (tiendaId <= 0) throw new ArgumentException("tiendaId inválido.");
            if (usuarioFinalId <= 0) throw new ArgumentException("usuarioFinalId inválido.");
            var clave = NormalizarOperacionId(operacionId);
            var nota = string.IsNullOrWhiteSpace(notaPedido) ? null : notaPedido.Trim();
            if (nota?.Length > 400) throw new ArgumentException("La nota no puede superar 400 caracteres.");
            // Mantener el hash del contrato simple ya emitido; incluir todas las filas nuevas.
            var payloadHash = Hash(destinos.Count == 1 && destinos[0].Instrucciones is null
                ? JsonSerializer.Serialize(new { tiendaId, usuarioFinalId, nota })
                : JsonSerializer.Serialize(new { tiendaId, destinatarios = destinos, nota }));

            // 2) Validar tienda pertenece al ClienteApp
            var tienda = await _db.Tiendas
                .AsNoTracking()
                .SingleOrDefaultAsync(t => t.Id == tiendaId && t.ClienteAppId == clienteAppId && t.Activo);

            if (tienda is null)
                throw new InvalidOperationException("La tienda no existe o no pertenece a este ClienteApp.");

            var ids = destinos.Select(d => d.UsuarioFinalId).Distinct().ToArray();
            var contactosPrevios = await _db.UsuariosFinales.AsNoTracking().Where(u => ids.Contains(u.Id)
                && u.TiendaId == tiendaId && u.Activo).ToDictionaryAsync(u => u.Id);
            contactosPrevios.TryGetValue(usuarioFinalId, out var uf);

            var cliente = await _db.ClientesApp.AsNoTracking().SingleOrDefaultAsync(c => c.Id == clienteAppId);
            if (cliente is null) throw new InvalidOperationException("ClienteApp no existe.");
            // Evitar llamadas a Maps en un reintento ya confirmado. La comprobación
            // definitiva de idempotencia se hace bajo el bloqueo del tenant.
            var hayOperacion = clave is not null && await _db.OperacionesOrden.AsNoTracking().AnyAsync(x =>
                x.ClienteAppId == clienteAppId && x.ActorIdentityUserId == actorIdentityUserId
                && x.Tipo == "CrearOrden" && x.Clave == clave);
            (double? distanciaKm, decimal? precio) = (null, null);
            // Resolver cada fuente una sola vez por solicitud (también un resultado
            // nulo). Reutilizar exactamente esas coordenadas para tarifa y snapshot,
            // sin modificar los contactos ni las órdenes históricas.
            var ubicaciones = new Dictionary<FuenteUbicacion, Coordenadas?>();
            async Task<Coordenadas?> ResolverAsync(string direccion, double? lat, double? lng)
            {
                var fuente = new FuenteUbicacion(direccion, lat, lng);
                if (ubicaciones.TryGetValue(fuente, out var cached)) return cached;
                Coordenadas? resolved = CoordenadaValida(lat, lng) ? new(lat!.Value, lng!.Value) : null;
                if (resolved is null)
                {
                    var geocode = await _googleMaps.GeocodificarAsync(direccion);
                    if (geocode is { } value && CoordenadaValida(value.Lat, value.Lng)) resolved = new(value.Lat, value.Lng);
                }
                ubicaciones.Add(fuente, resolved);
                return resolved;
            }
            Coordenadas? origen = null;
            var puntosDestino = new Dictionary<int, Coordenadas?>();
            if (!hayOperacion && contactosPrevios.Count == ids.Length)
            {
                origen = await ResolverAsync(tienda.Direccion, tienda.Latitud, tienda.Longitud);
                foreach (var contacto in contactosPrevios.Values)
                    puntosDestino[contacto.Id] = await ResolverAsync(contacto.DireccionUbicacion, contacto.Latitud, contacto.Longitud);
                if (destinos.Count == 1) (distanciaKm, precio) = await CalcularPrecioAsync(cliente, origen, puntosDestino[usuarioFinalId]);
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            // El UPDATE sin incremento bloquea antes de deduplicar. Así un reintento
            // concurrente no consume secuencia, cuota, códigos ni historial.
            var actualizadas = await _db.ClientesApp
                .Where(c => c.Id == clienteAppId && c.Estado == EstadoClienteApp.Activo)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OrdenSeq, c => c.OrdenSeq));
            if (actualizadas != 1) throw new InvalidOperationException("ClienteApp no activo.");

            var tiendaActual = await _db.Tiendas.AsNoTracking().SingleOrDefaultAsync(t =>
                t.Id == tiendaId && t.ClienteAppId == clienteAppId && t.Activo);
            if (tiendaActual is null) throw new InvalidOperationException("La tienda no está disponible.");
            if (actorRol == "Cliente" && !await _db.UsuariosTienda.AnyAsync(x =>
                x.IdentityUserId == actorIdentityUserId && x.TiendaId == tiendaId && x.Activo))
                throw new InvalidOperationException("No tienes acceso a esta tienda.");

            var repetida = await LeerOperacionAsync(clienteAppId, actorIdentityUserId, clave, payloadHash);
            if (repetida is not null)
            {
                await tx.CommitAsync();
                return repetida;
            }

            var contactos = await _db.UsuariosFinales.AsNoTracking().Where(u => ids.Contains(u.Id)
                && u.TiendaId == tiendaId && u.Activo).ToDictionaryAsync(u => u.Id);
            if (!contactos.TryGetValue(usuarioFinalId, out var ufActual))
                throw new OrdenSolicitudRechazadaException("El destinatario no existe, está inactivo o no pertenece a esta tienda.");
            if (contactos.Count != ids.Length) throw new OrdenSolicitudRechazadaException(
                "Uno de los destinatarios no existe, está inactivo o no pertenece a esta tienda.");
            try { await ValidarLimitePorPlanAsync(clienteAppId, DateTime.UtcNow); }
            catch (InvalidOperationException ex) { throw new OrdenSolicitudRechazadaException(ex.Message); }

            // Si dirección/coordenadas/tarifa cambiaron mientras Maps respondía,
            // cotizar de nuevo con los mismos datos que se van a congelar.
            var clienteActual = await _db.ClientesApp.AsNoTracking().SingleAsync(c => c.Id == clienteAppId);
            origen = await ResolverAsync(tiendaActual.Direccion, tiendaActual.Latitud, tiendaActual.Longitud);
            foreach (var contacto in contactos.Values)
                puntosDestino[contacto.Id] = await ResolverAsync(contacto.DireccionUbicacion, contacto.Latitud, contacto.Longitud);
            if (destinos.Count == 1 && (uf is null || hayOperacion || tienda.Direccion != tiendaActual.Direccion
                || tienda.Latitud != tiendaActual.Latitud || tienda.Longitud != tiendaActual.Longitud
                || uf.DireccionUbicacion != ufActual.DireccionUbicacion
                || uf.Latitud != ufActual.Latitud || uf.Longitud != ufActual.Longitud
                || cliente.PrecioBase != clienteActual.PrecioBase || cliente.PrecioPorKm != clienteActual.PrecioPorKm))
                (distanciaKm, precio) = await CalcularPrecioAsync(clienteActual, origen, puntosDestino[usuarioFinalId]);
            tienda = tiendaActual;
            uf = ufActual;

            await _db.ClientesApp.Where(c => c.Id == clienteAppId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.OrdenSeq, c => c.OrdenSeq + 1));
            var numeroA = await _db.ClientesApp.Where(c => c.Id == clienteAppId).Select(c => c.OrdenSeq).SingleAsync();
            var codigoA = await GenerarCodigoAAsync(clienteAppId, numeroA);

            // 6) Crear orden
            var orden = new OrdenEntrega
            {
                ClienteAppId = clienteAppId,
                TiendaId = tiendaId,
                UsuarioFinalId = usuarioFinalId,
                DatosEntregaCongelados = true,
                RecogidaNombre = tienda.Nombre,
                RecogidaDireccion = tienda.Direccion,
                RecogidaTelefono = tienda.Telefono,
                RecogidaLatitud = origen?.Latitud,
                RecogidaLongitud = origen?.Longitud,
                DestinoNombre = uf.Nombre,
                DestinoDireccion = uf.DireccionUbicacion,
                DestinoTelefono = uf.Telefono,
                DestinoMapaLink = uf.MapaLink,
                DestinoLatitud = puntosDestino[usuarioFinalId]?.Latitud,
                DestinoLongitud = puntosDestino[usuarioFinalId]?.Longitud,
                NumeroOrdenA = numeroA,
                CodigoA = codigoA,
                Estado = EstadoOrden.Creada,
                EsAuto = tienda.AutoAsignacion,
                Precio = precio,
                DistanciaKm = distanciaKm,
                NotaPedido = nota
            };

            if (destinos.Count > 1)
            {
                orden.UsuarioFinalId = null;
                orden.DestinoNombre = $"{destinos.Count} destinatarios";
                orden.DestinoDireccion = orden.DestinoTelefono = "";
                orden.DestinoMapaLink = null; orden.DestinoLatitud = orden.DestinoLongitud = null;
            }
            foreach (var (destino, posicion) in destinos.Select((d, i) => (d, i + 1)))
            {
                var contacto = contactos[destino.UsuarioFinalId];
                orden.Paradas.Add(new ParadaEntrega { ClienteAppId = clienteAppId, TiendaId = tiendaId,
                    UsuarioFinalId = contacto.Id, Posicion = posicion, Nombre = contacto.Nombre,
                    Direccion = contacto.DireccionUbicacion, Telefono = contacto.Telefono, MapaLink = contacto.MapaLink,
                    Latitud = puntosDestino[contacto.Id]?.Latitud, Longitud = puntosDestino[contacto.Id]?.Longitud,
                    Instrucciones = destino.Instrucciones });
            }
            _db.OrdenesEntrega.Add(orden);
            await _db.SaveChangesAsync();

            // 6) Crear B y C en tabla CodigoEntrega
            var codigoB = await GenerarCodigoAsync(clienteAppId, TipoCodigo.B_Recoleccion, codigoA);

            _db.CodigosEntrega.Add(new CodigoEntrega
            {
                OrdenEntregaId = orden.Id,
                ClienteAppId = clienteAppId,
                Tipo = TipoCodigo.B_Recoleccion,
                Codigo = codigoB
            });

            var excluidos = new List<string> { codigoA, codigoB };
            foreach (var parada in orden.Paradas.OrderBy(p => p.Posicion))
            {
                var codigoC = await GenerarCodigoAsync(clienteAppId, TipoCodigo.C_Finalizacion, excluidos.ToArray());
                excluidos.Add(codigoC);
                orden.Codigos.Add(new CodigoEntrega { OrdenEntregaId = orden.Id, ClienteAppId = clienteAppId,
                    Tipo = TipoCodigo.C_Finalizacion, Codigo = codigoC, ParadaEntregaId = parada.Id });
            }

            if (clave is not null)
                _db.OperacionesOrden.Add(new OperacionOrden
                {
                    ClienteAppId = clienteAppId, ActorIdentityUserId = actorIdentityUserId,
                    Clave = clave, PayloadHash = payloadHash, OrdenEntregaId = orden.Id
                });

            // 7) Historial
            RegistrarHistorial(                orden.Id,
                TipoEventoOrden.Creada,
                actorIdentityUserId,
                actorRol,
                $"Orden creada. A={codigoA}. Códigos B y C generados.");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();

            return ResultadoCreacion(orden);
        }

        public async Task AsignarPilotoAsync(string codigoB, int pilotoId, string actorIdentityUserId, string actorRol, int? ordenId = null)
        {
            if (string.IsNullOrWhiteSpace(codigoB)) throw new ArgumentException("codigoB requerido.");
            if (pilotoId <= 0) throw new ArgumentException("pilotoId inválido.");

            // Buscar orden por el código B
            var codeB = await CodigoBQuery(codigoB, ordenId)
                .SingleOrDefaultAsync(c => c.OrdenEntrega.Estado == EstadoOrden.Creada
                    && c.OrdenEntrega.ClienteApp.GestorIdentityUserId == actorIdentityUserId);

            if (codeB?.OrdenEntrega is null)
                throw new InvalidOperationException("Código B inválido.");

            var orden = codeB.OrdenEntrega;

            using var tx = await _quotas.BeginTenantMutationAsync(orden.ClienteAppId);
            await _db.Entry(orden).ReloadAsync();
            if (orden.GestionPorParadas) throw new OperationsException(426, "La asignación de esta orden se administra por parada.");

            // Validar piloto pertenece al mismo ClienteApp
            if (!await _db.ClientesApp.AnyAsync(c => c.Id == orden.ClienteAppId
                && c.GestorIdentityUserId == actorIdentityUserId && c.Estado == EstadoClienteApp.Activo))
                throw new InvalidOperationException("El gestor no tiene acceso a esta orden.");

            var piloto = await _db.Pilotos.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == pilotoId && p.ClienteAppId == orden.ClienteAppId && p.Activo
                    && p.ClienteApp.Estado == EstadoClienteApp.Activo);

            if (piloto is null)
                throw new InvalidOperationException("El piloto no existe o no pertenece al mismo ClienteApp.");

            if (orden.Estado != EstadoOrden.Creada)
                throw new InvalidOperationException("Solo se puede asignar si la orden está en estado Creada.");
            var asignacion = Task.CompletedTask;
            ValidarAsignacionOperativa(orden.ClienteAppId, orden.Id, pilotoId, ref asignacion);
            await asignacion;

            var now = DateTime.UtcNow;
            var asignadas = await _db.OrdenesEntrega
                .Where(o => o.Id == orden.Id && o.ClienteAppId == piloto.ClienteAppId
                    && o.Estado == EstadoOrden.Creada && o.PilotoId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PilotoId, pilotoId)
                    .SetProperty(o => o.AssignedAt, now).SetProperty(o => o.Estado, EstadoOrden.Asignada));
            if (asignadas != 1)
                throw new InvalidOperationException("La orden ya fue asignada por otra solicitud.");
            await RefrescarOrdenLocalAsync(orden.Id);

            RegistrarHistorial(
                orden.Id,
                TipoEventoOrden.Asignada,
                actorIdentityUserId,
                actorRol,
                $"Orden asignada a pilotoId={pilotoId}.");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task ConfirmarRecolectaAsync(string codigoB, string actorIdentityUserId, string actorRol,
            int? ordenId = null, string? operacionId = null)
        {
            var clave = NormalizarOperacionId(operacionId);
            if (!ordenId.HasValue && codigoB?.Length == 4)
                throw new InvalidOperationException("Los códigos cortos requieren seleccionar la orden.");
            var id = ordenId ?? await _db.CodigosEntrega.AsNoTracking().Where(c => c.Codigo == codigoB
                && c.Tipo == TipoCodigo.B_Recoleccion && c.OrdenEntrega.Piloto != null
                && c.OrdenEntrega.Piloto.IdentityUserId == actorIdentityUserId)
                .Select(c => c.OrdenEntregaId).SingleOrDefaultAsync();
            if (await _db.OrdenesEntrega.AsNoTracking().AnyAsync(o => o.Id == id && o.GestionPorParadas
                && _db.Pilotos.Any(p => p.IdentityUserId == actorIdentityUserId && p.ClienteAppId == o.ClienteAppId
                    && p.Activo && p.ClienteApp.Estado == EstadoClienteApp.Activo)))
            {
                await ReproducirRecogidaLegadaGestionadaAsync(id, codigoB, actorIdentityUserId, clave);
                return;
            }
            var orden = await CargarOrdenAutorizadaAsync(id, actorIdentityUserId);
            if (orden.GestionPorParadas) throw new OperationsException(426, "Utiliza la recogida por lote con la revisión de cada parada.");
            using var tx = await _quotas.BeginTenantMutationAsync(orden.ClienteAppId);
            orden = await CargarOrdenAutorizadaAsync(id, actorIdentityUserId);
            if (orden.GestionPorParadas) throw new OperationsException(426, "Utiliza la recogida por lote con la revisión de cada parada.");
            var hash = Hash(JsonSerializer.Serialize(new { ordenId = id, codigoB }));
            var repetida = await OperacionConfirmacionAsync(orden.ClienteAppId, actorIdentityUserId, "ConfirmarRecogida", clave, hash);
            if (repetida is not null) { await tx.CommitAsync(); return; }
            if (orden.Estado != EstadoOrden.Asignada)
                throw new OrdenSolicitudRechazadaException("La orden no está asignada para recoger.");
            var trabajo = Task.CompletedTask;
            ValidarTrabajoOperativo(orden.ClienteAppId, orden.Id, null, true, ref trabajo);
            await trabajo;
            var codeB = await _db.CodigosEntrega.SingleOrDefaultAsync(c => c.OrdenEntregaId == id
                && c.ClienteAppId == orden.ClienteAppId && c.Tipo == TipoCodigo.B_Recoleccion
                && c.Codigo == codigoB && !c.Usado && c.Reservado);
            if (codeB is null) throw new OrdenSolicitudRechazadaException("Código B inválido para esta orden.");
            var now = DateTime.UtcNow;
            var rows = await _db.OrdenesEntrega.Where(o => o.Id == id && o.Estado == EstadoOrden.Asignada
                && o.PilotoId == orden.PilotoId).ExecuteUpdateAsync(v => v.SetProperty(o => o.Estado, EstadoOrden.Recolectada)
                    .SetProperty(o => o.RecolectadaAt, now));
            if (rows != 1) throw new InvalidOperationException("La orden ya cambió de estado.");
            await RefrescarOrdenLocalAsync(id);
            codeB.Usado = true; codeB.UsadoAt = now;
            RegistrarHistorial(id, TipoEventoOrden.Recolectada, actorIdentityUserId, actorRol, "Recolecta confirmada con código B.");
            GuardarConfirmacion(orden.ClienteAppId, id, actorIdentityUserId, "ConfirmarRecogida", clave, hash, new { ok = true });
            await _db.SaveChangesAsync(); await tx.CommitAsync();
        }

        public async Task ConfirmarEntregaAsync(string codigoB, string codigoC, string actorIdentityUserId,
            string actorRol, int? ordenId = null)
        {
            OrdenEntrega orden;
            if (ordenId.HasValue && string.IsNullOrWhiteSpace(codigoB))
                orden = await CargarOrdenAutorizadaAsync(ordenId.Value, actorIdentityUserId);
            else
            {
                var code = await CodigoBQuery(codigoB, ordenId).AsNoTracking().SingleOrDefaultAsync(c =>
                    c.OrdenEntrega.Piloto != null && c.OrdenEntrega.Piloto.IdentityUserId == actorIdentityUserId);
                if (code is null) throw new InvalidOperationException("Código B inválido.");
                orden = await CargarOrdenAutorizadaAsync(code.OrdenEntregaId, actorIdentityUserId);
            }
            int paradaId;
            using (var tx = await _quotas.BeginTenantMutationAsync(orden.ClienteAppId))
            {
                orden = await CargarOrdenAutorizadaAsync(orden.Id, actorIdentityUserId);
                if (orden.GestionPorParadas) throw new OperationsException(426, "Confirma cada parada con su asignación vigente.");
                paradaId = (await AsegurarParadaLegadaAsync(orden)).Id;
                await tx.CommitAsync();
            }
            await ConfirmarParadaCoreAsync(orden.Id, paradaId, codigoC, actorIdentityUserId, null);
        }

        private async Task ValidarPilotoAsignadoAsync(OrdenEntrega orden, string identityUserId)
        {
            if (orden.GestionPorParadas) throw new OperationsException(426, "Esta orden se administra por parada.");
            var autorizado = await _db.Pilotos.AnyAsync(p => p.Id == orden.PilotoId
                && p.IdentityUserId == identityUserId && p.ClienteAppId == orden.ClienteAppId
                && p.Activo && p.ClienteApp.Estado == EstadoClienteApp.Activo);
            if (!autorizado) throw new InvalidOperationException("La orden no pertenece a este piloto.");
        }

        private async Task RefrescarOrdenLocalAsync(int ordenId)
        {
            // ExecuteUpdate no actualiza el tracker. Una siguiente operación en
            // el mismo scope debe observar el estado que acabamos de guardar.
            var local = _db.OrdenesEntrega.Local.FirstOrDefault(o => o.Id == ordenId);
            if (local is not null) await _db.Entry(local).ReloadAsync();
        }

        public async Task ValidarLimitePorPlanAsync(int clienteAppId, DateTime fechaUtc)
        {
            await _quotas.EnsureSubscriptionAllowsNewWorkAsync(clienteAppId);
            var fechaLocal = fechaUtc.AddHours(-4);
            var inicioMes = DateTime.SpecifyKind(new DateTime(fechaLocal.Year, fechaLocal.Month, 1).AddHours(4), DateTimeKind.Utc);
            var finMes = inicioMes.AddMonths(1);

            var cliente = await _db.ClientesApp
                .AsNoTracking()
                .Include(c => c.Plan)
                .SingleOrDefaultAsync(c => c.Id == clienteAppId);

            if (cliente is null) throw new InvalidOperationException("ClienteApp no existe.");
            if (cliente.Plan is null) throw new InvalidOperationException("ClienteApp no tiene plan asignado.");

            var limite = (await _quotas.ResolveAsync(clienteAppId)).MaxOrdenesMes;
            if (limite < 0)
                throw new InvalidOperationException("La cuota mensual de la empresa no está configurada correctamente.");

            var usadas = await _db.OrdenesEntrega.CountAsync(o =>
                o.ClienteAppId == clienteAppId &&
                o.CreatedAt >= inicioMes &&
                o.CreatedAt < finMes);

            if (limite > 0 && usadas >= limite)
                throw new InvalidOperationException("Límite mensual de órdenes alcanzado según el plan.");
        }

        public async Task<OrdenPilotoView> ObtenerOrdenParaPilotoAsync(string codigoB, int pilotoId, int? ordenId = null)
        {
            if (string.IsNullOrWhiteSpace(codigoB)) throw new ArgumentException("codigoB requerido.");
            if (pilotoId <= 0) throw new ArgumentException("pilotoId inválido.");

            var codeB = await CodigoBQuery(codigoB, ordenId)
                .Include(c => c.OrdenEntrega)
                    .ThenInclude(o => o.UsuarioFinal)
                .SingleOrDefaultAsync(c => c.OrdenEntrega.PilotoId == pilotoId
                    && c.OrdenEntrega.Piloto!.Activo
                    && c.OrdenEntrega.Piloto.ClienteAppId == c.OrdenEntrega.ClienteAppId
                    && c.OrdenEntrega.ClienteApp.Estado == EstadoClienteApp.Activo);

            if (codeB?.OrdenEntrega is null)
                throw new InvalidOperationException("Código B inválido.");

            var orden = codeB.OrdenEntrega;

            if (orden.PilotoId != pilotoId)
                throw new InvalidOperationException("Esta orden no está asignada a ese piloto.");
            if (orden.GestionPorParadas) throw new OperationsException(426, "Consulta sólo las paradas de tu asignación vigente.");

            // 🔒 Regla: no ve destino hasta recolectar
            var puedeVer = orden.Estado == EstadoOrden.Recolectada || orden.Estado == EstadoOrden.Entregada;

            return new OrdenPilotoView(
                OrdenId: orden.Id,
                NumeroOrdenA: orden.NumeroOrdenA,
                Estado: orden.Estado.ToString(),
                PuedeVerDestino: puedeVer,
                DestinoDireccion: puedeVer ? (orden.DatosEntregaCongelados ? orden.DestinoDireccion : orden.UsuarioFinal.DireccionUbicacion) : null,
                NombreUsuarioFinal: puedeVer ? (orden.DatosEntregaCongelados ? orden.DestinoNombre : orden.UsuarioFinal.Nombre) : null,
                TelefonoUsuarioFinal: puedeVer ? (orden.DatosEntregaCongelados ? orden.DestinoTelefono : orden.UsuarioFinal.Telefono) : null,
                CodigoA: orden.CodigoA
            );
        }

        // -------------------------
        // Auto-asignación (pilotos aceptan/rechazan)
        // -------------------------

        public async Task<List<OrdenAutoDisponibleDto>> GetOrdenesAutoDisponiblesAsync(int pilotoId)
        {
            var piloto = await _db.Pilotos.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == pilotoId && p.Activo && p.ClienteApp.Estado == EstadoClienteApp.Activo);

            if (piloto is null || !piloto.EnLinea)
                return new List<OrdenAutoDisponibleDto>();

            var rechazadas = _db.OrdenesAutoRechazo
                .Where(r => r.PilotoId == pilotoId)
                .Select(r => r.OrdenEntregaId);

            IQueryable<OrdenEntrega> ofertas = _db.OrdenesEntrega.AsNoTracking();
            FiltrarOfertasOperativas(ref ofertas);
            return await ofertas
                .Include(o => o.Tienda)
                .Include(o => o.UsuarioFinal)
                .Where(o => o.ClienteAppId == piloto.ClienteAppId
                         && !o.GestionPorParadas
                         && o.EsAuto
                         && o.Estado == EstadoOrden.Creada
                         && o.PilotoId == null
                         && !rechazadas.Contains(o.Id))
                .OrderBy(o => o.CreatedAt)
                .Select(o => new OrdenAutoDisponibleDto(
                    o.Id,
                    o.NumeroOrdenA,
                    o.DatosEntregaCongelados ? o.RecogidaNombre : o.Tienda.Nombre,
                    o.DatosEntregaCongelados ? o.RecogidaDireccion : o.Tienda.Direccion,
                    "",
                    o.NotaPedido,
                    o.CreatedAt,
                    o.DistanciaKm,
                    o.CodigoA,
                    o.Paradas.Count == 0 ? 1 : o.Paradas.Count))
                .ToListAsync();
        }

        public async Task AceptarOrdenAutoAsync(int ordenId, int pilotoId, string actorIdentityUserId, string actorRol)
        {
            if (ordenId <= 0) throw new ArgumentException("ordenId inválido.");
            if (pilotoId <= 0) throw new ArgumentException("pilotoId inválido.");

            var tenantId = await _db.Pilotos.AsNoTracking()
                .Where(p => p.Id == pilotoId && p.IdentityUserId == actorIdentityUserId)
                .Select(p => p.ClienteAppId).SingleOrDefaultAsync();
            if (tenantId == 0) throw new InvalidOperationException("Piloto no disponible.");
            using var tx = await _quotas.BeginTenantMutationAsync(tenantId);
            // La fila del piloto coordina aceptación y cambios de disponibilidad.
            // Varias órdenes distintas pueden asignarse al mismo piloto; cada
            // orden conserva su UPDATE condicional y las guardas de su ruta.
            var disponible = await _db.Pilotos
                .Where(p => p.Id == pilotoId && p.IdentityUserId == actorIdentityUserId && p.Activo && p.EnLinea
                    && p.ClienteApp.Estado == EstadoClienteApp.Activo)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EnLinea, true));
            if (disponible != 1)
                throw new InvalidOperationException("El piloto debe estar activo y en línea.");

            var piloto = await _db.Pilotos.AsNoTracking().SingleAsync(p => p.Id == pilotoId);

            if (piloto is null)
                throw new InvalidOperationException("Piloto no encontrado o inactivo.");

            var asignacion = Task.CompletedTask;
            ValidarAsignacionOperativa(tenantId, ordenId, pilotoId, ref asignacion);
            await asignacion;

            // Tenant y disponibilidad forman parte del UPDATE, no solo de la consulta de ofertas.
            var now = DateTime.UtcNow;
            var rows = await _db.OrdenesEntrega
                .Where(o => o.Id == ordenId && o.ClienteAppId == piloto.ClienteAppId && o.EsAuto
                    && !o.GestionPorParadas
                    && o.Estado == EstadoOrden.Creada && o.PilotoId == null
                    && !_db.OrdenesAutoRechazo.Any(r => r.OrdenEntregaId == o.Id && r.PilotoId == pilotoId))
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.PilotoId, pilotoId)
                    .SetProperty(o => o.Estado, EstadoOrden.Asignada).SetProperty(o => o.AssignedAt, now));

            if (rows == 0)
                throw new InvalidOperationException("La orden ya fue tomada por otro piloto o ya no está disponible.");
            await RefrescarOrdenLocalAsync(ordenId);

            RegistrarHistorial(
                ordenId,
                TipoEventoOrden.Asignada,
                actorIdentityUserId,
                actorRol,
                $"Orden auto-asignada (aceptada por el piloto). PilotoId={pilotoId}.");

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        public async Task RechazarOrdenAutoAsync(int ordenId, int pilotoId)
        {
            if (ordenId <= 0) return;
            if (pilotoId <= 0) return;

            var tenantId = await _db.Pilotos.AsNoTracking().Where(p => p.Id == pilotoId)
                .Select(p => p.ClienteAppId).SingleOrDefaultAsync();
            if (tenantId == 0) throw new InvalidOperationException("Piloto no disponible.");
            using var tx = await _quotas.BeginTenantMutationAsync(tenantId);
            var disponible = await _db.Pilotos.Where(p => p.Id == pilotoId && p.Activo && p.EnLinea
                    && p.ClienteApp.Estado == EstadoClienteApp.Activo)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.EnLinea, true));
            if (disponible != 1) throw new InvalidOperationException("Piloto no disponible.");
            var tenant = await _db.Pilotos.Where(p => p.Id == pilotoId).Select(p => p.ClienteAppId).SingleAsync();
            var ordenDisponible = await _db.OrdenesEntrega.AsNoTracking()
                .AnyAsync(o => o.Id == ordenId && o.ClienteAppId == tenant && o.EsAuto &&
                               !o.GestionPorParadas && o.Estado == EstadoOrden.Creada && o.PilotoId == null);

            if (!ordenDisponible) throw new InvalidOperationException("Orden no disponible para este piloto.");

            var ya = await _db.OrdenesAutoRechazo
                .AnyAsync(r => r.OrdenEntregaId == ordenId && r.PilotoId == pilotoId);

            if (ya) return;

            _db.OrdenesAutoRechazo.Add(new OrdenAutoRechazo
            {
                OrdenEntregaId = ordenId,
                PilotoId = pilotoId
            });

            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // -------------------------
        // Helpers
        // -------------------------

        private readonly record struct FuenteUbicacion(string Direccion, double? Latitud, double? Longitud);
        private readonly record struct Coordenadas(double Latitud, double Longitud);
        private static bool CoordenadaValida(double? lat, double? lng) => lat.HasValue && lng.HasValue
            && double.IsFinite(lat.Value) && double.IsFinite(lng.Value) && lat.Value is >= -90 and <= 90 && lng.Value is >= -180 and <= 180;

        private async Task<(double? km, decimal precio)> CalcularPrecioAsync(ClienteApp cliente, Coordenadas? origen, Coordenadas? destino)
        {
            var basePrecio = cliente.PrecioBase;

            if (cliente.PrecioPorKm <= 0 || origen is null || destino is null)
                return (null, basePrecio);

            // Distancia real de ruta (Routes API); si falla, línea recta
            var km = await _googleMaps.DistanciaRutaKmAsync(origen.Value.Latitud, origen.Value.Longitud, destino.Value.Latitud, destino.Value.Longitud)
                     ?? _googleMaps.CalcularDistanciaKm(origen.Value.Latitud, origen.Value.Longitud, destino.Value.Latitud, destino.Value.Longitud);

            var precio = basePrecio + (decimal)km * cliente.PrecioPorKm;
            return (km, Math.Round(precio, 2));
        }

        private IQueryable<CodigoEntrega> CodigoBQuery(string codigoB, int? ordenId)
        {
            if (!ordenId.HasValue && codigoB.Length == 4)
                throw new InvalidOperationException("Actualiza la app: los códigos de cuatro dígitos requieren seleccionar la orden.");
            return _db.CodigosEntrega.Include(c => c.OrdenEntrega)
                .Where(c => c.Codigo == codigoB && c.Tipo == TipoCodigo.B_Recoleccion
                    && (ordenId == null || c.OrdenEntregaId == ordenId));
        }

        private static string? NormalizarOperacionId(string? clave)
        {
            if (clave is null) return null; // compatibilidad con clientes anteriores
            clave = clave.Trim();
            if (clave.Length is < 1 or > 64)
                throw new ArgumentException("OperacionId debe ser un GUID o una cadena hexadecimal de hasta 64 caracteres.");
            if (Guid.TryParse(clave, out var guid)) return guid.ToString("N");
            if (!Regex.IsMatch(clave, "^[a-fA-F0-9]+$"))
                throw new ArgumentException("OperacionId debe ser un GUID o una cadena hexadecimal de hasta 64 caracteres.");
            return clave.ToLowerInvariant();
        }

        private async Task<OrdenCreadaResult?> LeerOperacionAsync(int tenantId, string actorId, string? clave, string hash)
        {
            if (clave is null) return null;
            var operacion = await _db.OperacionesOrden.AsNoTracking()
                .Include(x => x.OrdenEntrega).ThenInclude(o => o.Codigos)
                .Include(x => x.OrdenEntrega).ThenInclude(o => o.Paradas)
                .SingleOrDefaultAsync(x => x.ClienteAppId == tenantId && x.ActorIdentityUserId == actorId
                    && x.Tipo == "CrearOrden" && x.Clave == clave);
            if (operacion is null) return null;
            if (operacion.PayloadHash != hash)
                throw new OperacionOrdenConflictoException("Esta operación ya creó otra solicitud. Conserva la referencia y revisa el pedido original.");
            return ResultadoCreacion(operacion.OrdenEntrega);
        }

        private async Task<string> GenerarCodigoAAsync(int tenantId, int secuencia)
        {
            var abiertas = await _db.OrdenesEntrega.AsNoTracking()
                .Where(o => o.ClienteAppId == tenantId && !EstadosEntrega.Terminales.Contains(o.Estado))
                .Select(o => new { o.CodigoA, o.NumeroOrdenA }).ToListAsync();
            var reservados = abiertas.Select(o => o.CodigoA
                ?? o.NumeroOrdenA.ToString("D4", CultureInfo.InvariantCulture)).ToHashSet();
            return CodigoDisponible(reservados, secuencia % 10000);
        }

        private async Task<string> GenerarCodigoAsync(int tenantId, TipoCodigo tipo, params string[] excluir)
        {
            var reservados = (await _db.CodigosEntrega.AsNoTracking()
                .Where(c => c.ClienteAppId == tenantId && c.Tipo == tipo && c.Reservado)
                .Select(c => c.Codigo).ToListAsync()).ToHashSet();
            reservados.UnionWith(excluir);
            return CodigoDisponible(reservados, RandomNumberGenerator.GetInt32(10000));
        }

        private static string CodigoDisponible(HashSet<string> reservados, int inicio)
        {
            // Recorrido acotado: nunca bloquear una solicitud si se agota el espacio.
            for (var i = 0; i < 10000; i++)
            {
                var codigo = ((inicio + i) % 10000).ToString("D4", CultureInfo.InvariantCulture);
                if (!reservados.Contains(codigo)) return codigo;
            }
            throw new InvalidOperationException("No quedan códigos de cuatro dígitos disponibles. Finaliza las órdenes pendientes antes de crear otra.");
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
