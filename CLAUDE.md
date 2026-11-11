# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build
dotnet build Login.sln

# Run (dev server at http://localhost:5008)
dotnet run --project Login/Login.csproj

# Apply migrations
cd Login && dotnet ef database update

# Add a new migration
cd Login && dotnet ef migrations add <MigrationName>
```

Integration tests are in `Login.Tests`. Run `dotnet test Login.sln` for isolated
SQLite tests. To also test SQL Server LocalDB, create/start the disposable
`PilotGoCodexQA_20261109` instance and set `PILOTGO_TEST_SQLSERVER=1` before running
the same tests. Tests use generated `PilotGoTest_*` databases, never
`DefaultConnection`, and skip application seeding in the `Testing` environment.
See `docs/ANALISIS-SESION-ORDENES-MULTITENANT-20261109.md` for the mobile flow and QA.
See `docs/RELEASE-1.1.0.md` before deployment: four-digit codes require migration
`20261110033858_CodigosCuatroDigitos` and an updated mobile client. Production
requires a private `Jwt__Key` and does not create demo accounts automatically.
The next reliability batch is documented in `docs/CONFIABILIDAD-1.2.0.md`.
It requires migration `20261111222204_ConfiabilidadOrdenesSnapshot` and adds
store-scoped requests, durable create-operation IDs and immutable delivery data.
Upgrade the backend/database before distributing the matching mobile client.

## Database

SQL Server connection targets `Server=srSkimberlyn;Database=LoginDB`. The `ApplicationDbContext` extends `IdentityDbContext<IdentityUser>`. On first run, `SeedData.cs` auto-creates roles and test users:

| Email | Password | Role |
|---|---|---|
| superadmin@local.com | Admin123 | SuperAdmin |
| gestor@local.com | Admin123 | Gestor |
| cliente@local.com | Admin123 | Cliente |
| piloto@local.com | Admin123 | Piloto |
| admin@local.com | Admin123 | All roles |

## Architecture Overview

This is a **multi-tenant SaaS delivery management platform** (PilotoGot). Tenants are `ClienteApp` entities linked to a `Plan` that defines limits (max stores, pilots, orders/month).

### Role Hierarchy

- **SuperAdmin** — platform owner; manages `Plan`s and `ClienteApp` tenants
- **Gestor** — tenant admin; manages stores (`Tienda`), users, pilots (`Piloto`)
- **Cliente** — store employee; creates and tracks delivery orders
- **Piloto** — delivery driver; receives and completes deliveries via QR scan

### Order Lifecycle (`OrdenEntrega`)

```
Creada → Asignada → Recolectada → Entregada
               ↑           ↑           ↑
         Piloto assigned  Scan B code  Scan B+C codes
```

New orders display four-digit A/B/C strings; existing codes remain unchanged.
Short-code validation also requires the permanent order ID. B/C are reserved
per tenant/type until delivery and can then be reused without losing history.
Each order has two verification codes:
- **Code B** (`B_Recoleccion`) — scanned at pickup; only then is the delivery address revealed to the pilot
- **Code C** (`C_Finalizacion`) — scanned at delivery along with B to finalize

### Key Services

- **`IOrdenService`** (`Application/Services/OrdenService.cs`) — all order operations: creation (assigns sequential `NumeroOrdenA` per tenant), pilot assignment, pickup/delivery confirmation, plan limit checks
- **`IUserContextService`** (`Application/Services/UserContextService.cs`) — tracks the active `Tienda` per user session (stored in `UserContext` table)
- **`ClienteAppEstadoFilter`** (`Filters/`) — global action filter that blocks Gestor/Cliente/Piloto roles if their `ClienteApp.Estado` is not `Activo` (Suspendido/Vencido), redirecting to `BloqueoController`

### Controller → Role Mapping

| Controller | Role | Responsibility |
|---|---|---|
| `SuperAdminController` | SuperAdmin | Plans and tenant CRUD |
| `SupervisorController` | Gestor | Stores, users, pilots, orders |
| `GestorController` | Gestor | Secondary Gestor interface |
| `TiendaController` | Cliente | End-user management, order creation |
| `PilotoController` | Piloto | View assigned order, confirm pickup/delivery |
| `QrController` | Any auth | Generate QR PNG for a code string |
| `BloqueoController` | Any auth | Tenant lockout page |

### Enums (`Domain/Enums/Enums.cs`)

- `EstadoClienteApp`: Activo, Suspendido, Vencido
- `EstadoOrden`: Creada, Asignada, Recolectada, Entregada, Incidente
- `TipoCodigo`: B_Recoleccion, C_Finalizacion
- `EstadoPiloto`: Disponible, Ocupado, Inactivo

### Important DB Constraints

- `(ClienteAppId, NumeroOrdenA)` — unique per tenant (sequential order number)
- `(OrdenEntregaId, TipoCodigo)` — one B and one C code per order
- `(IdentityUserId, TiendaId)` — one `UsuarioTienda` record per user/store pair
- All FK relationships use `DeleteBehavior.Restrict` to prevent cascading deletes
