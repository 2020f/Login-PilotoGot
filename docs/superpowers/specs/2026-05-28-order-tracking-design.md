# Order Tracking in Tienda Order Cards

## Summary

Add a progress bar tracking component inside each order card in `Tienda/Ordenes.cshtml` showing the delivery lifecycle from pilot assignment to final delivery.

## Design (Option C — Progress Bar)

The tracker appears only on orders in state `Asignada`, `Recolectada`, or `Entregada`. Orders in `Creada` state show no tracker (no pilot yet).

**Visual:** A percentage progress bar with 3 milestone labels underneath showing real timestamps.

| State | Progress | Milestone shown |
|---|---|---|
| Asignada | 33% | 🛵 Asignado + AssignedAt |
| Recolectada | 67% | 🛵 Asignado + 📦 Recogido + timestamps |
| Entregada | 100% | All 3 + ✅ Entregado + EntregadaAt |
| Incidente | — | Same as current step + incidente badge |

## Changes

### 1. `Login/ViewModels/Tienda/OrdenTiendaRowVm.cs`
Add fields:
- `int EstadoNum` — integer value of `EstadoOrden` enum for view comparison
- `DateTime? AssignedAt`
- `DateTime? RecolectadaAt`
- `DateTime? EntregadaAt`

### 2. `Login/Controllers/TiendaController.cs` — `Ordenes` action
Map the new fields from the EF query (no extra joins needed, all on `OrdenEntrega`).

### 3. `Login/Views/Tienda/Ordenes.cshtml`
Inside the `.tp-order-body` div, after the existing content, add the tracking block conditionally when `EstadoNum >= 2` (Asignada).

Progress bar gradient: `#3b82f6 → #60a5fa`. Pending milestones shown at 40% opacity.
