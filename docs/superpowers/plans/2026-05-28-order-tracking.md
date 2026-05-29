# Order Tracking in Tienda Order Cards — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar una barra de progreso con hitos (Asignado → Recogido → Entregado) dentro de cada tarjeta de orden en la vista `Tienda/Ordenes`.

**Architecture:** Se extiende `OrdenTiendaRowVm` con los timestamps de cada etapa, el controlador mapea esos campos desde EF, y la vista renderiza condicionalmente la barra de progreso cuando el estado es ≥ Asignada (2). No hay cambios a la base de datos ni a los servicios.

**Tech Stack:** ASP.NET Core 8, Razor Views (.cshtml), C#, Entity Framework Core

---

### Task 1: Extender el ViewModel con campos de tracking

**Files:**
- Modify: `Login/ViewModels/Tienda/OrdenTiendaRowVm.cs`

- [ ] **Step 1: Agregar los nuevos campos al ViewModel**

Reemplazar el contenido de `Login/ViewModels/Tienda/OrdenTiendaRowVm.cs` con:

```csharp
using System;

namespace Login.ViewModels.Tienda
{
    public class OrdenTiendaRowVm
    {
        public int OrdenId { get; set; }
        public int NumeroOrdenA { get; set; }
        public string Estado { get; set; } = "";
        public int EstadoNum { get; set; }
        public string UsuarioFinal { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        // Tracking timestamps
        public DateTime? AssignedAt { get; set; }
        public DateTime? RecolectadaAt { get; set; }
        public DateTime? EntregadaAt { get; set; }
    }
}
```

- [ ] **Step 2: Verificar que compila**

```bash
dotnet build Login/Login.csproj
```

Esperado: `Build succeeded` sin errores.

---

### Task 2: Mapear los nuevos campos en el controlador

**Files:**
- Modify: `Login/Controllers/TiendaController.cs` (acción `Ordenes`, líneas ~444-459)

- [ ] **Step 1: Actualizar el Select en la consulta EF**

En `TiendaController.cs`, localizar la acción `Ordenes` (método `GET`). Reemplazar el bloque `.Select(o => new OrdenTiendaRowVm { ... })` con:

```csharp
.Select(o => new OrdenTiendaRowVm
{
    OrdenId = o.Id,
    NumeroOrdenA = o.NumeroOrdenA,
    Estado = o.Estado.ToString(),
    EstadoNum = (int)o.Estado,
    UsuarioFinal = o.UsuarioFinal.Nombre,
    CreatedAt = o.CreatedAt,
    AssignedAt = o.AssignedAt,
    RecolectadaAt = o.RecolectadaAt,
    EntregadaAt = o.EntregadaAt
})
```

- [ ] **Step 2: Verificar que compila**

```bash
dotnet build Login/Login.csproj
```

Esperado: `Build succeeded` sin errores.

---

### Task 3: Agregar el tracking a la vista de órdenes

**Files:**
- Modify: `Login/Views/Tienda/Ordenes.cshtml`

- [ ] **Step 1: Agregar el bloque de tracking dentro del card de cada orden**

En `Ordenes.cshtml`, localizar el bloque del card de orden (dentro del `foreach`). El `tp-order-body` actualmente termina con:

```html
                            <div class="tp-order-sub">
                                Creada: @o.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy hh:mm tt")
                            </div>
                        </div>
```

Agregar el bloque de tracking justo después del cierre del `tp-order-sub` y antes del cierre del `tp-order-body`:

```html
                            <div class="tp-order-sub">
                                Creada: @o.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy hh:mm tt")
                            </div>

                            @if (o.EstadoNum >= 2)
                            {
                                var pct = o.EstadoNum >= 4 ? 100
                                        : o.EstadoNum >= 3 ? 67
                                        : 33;

                                <div class="tp-tracking">
                                    <div class="tp-tracking-bar-wrap">
                                        <div class="tp-tracking-bar-fill" style="width:@(pct)%"></div>
                                    </div>
                                    <div class="tp-tracking-steps">
                                        <div class="tp-tracking-step @(o.EstadoNum >= 2 ? "tp-step-done" : "tp-step-pending")">
                                            <span class="tp-step-icon">🛵</span>
                                            <span class="tp-step-label">Asignado</span>
                                            <span class="tp-step-time">@(o.AssignedAt.HasValue ? o.AssignedAt.Value.ToLocalTime().ToString("hh:mm tt") : "—")</span>
                                        </div>
                                        <div class="tp-tracking-step @(o.EstadoNum >= 3 ? "tp-step-done" : "tp-step-pending")">
                                            <span class="tp-step-icon">📦</span>
                                            <span class="tp-step-label">Recogido</span>
                                            <span class="tp-step-time">@(o.RecolectadaAt.HasValue ? o.RecolectadaAt.Value.ToLocalTime().ToString("hh:mm tt") : "—")</span>
                                        </div>
                                        <div class="tp-tracking-step @(o.EstadoNum >= 4 ? "tp-step-done" : "tp-step-pending")">
                                            <span class="tp-step-icon">✅</span>
                                            <span class="tp-step-label">Entregado</span>
                                            <span class="tp-step-time">@(o.EntregadaAt.HasValue ? o.EntregadaAt.Value.ToLocalTime().ToString("hh:mm tt") : "—")</span>
                                        </div>
                                    </div>
                                </div>
                            }
                        </div>
```

- [ ] **Step 2: Agregar los estilos CSS del tracking**

Localizar la etiqueta `</div>` de cierre del `tp-list` (cerca del final del archivo, antes de `<div id="tpModalHost">`). Agregar el bloque `<style>` justo antes del `<div id="tpModalHost">`:

```html
<style>
    .tp-tracking {
        margin-top: 10px;
        padding-top: 10px;
        border-top: 1px solid rgba(255,255,255,.06);
    }

    .tp-tracking-bar-wrap {
        background: #374151;
        border-radius: 4px;
        height: 5px;
        overflow: hidden;
        margin-bottom: 8px;
    }

    .tp-tracking-bar-fill {
        height: 100%;
        background: linear-gradient(90deg, #3b82f6, #60a5fa);
        border-radius: 4px;
        transition: width .4s ease;
    }

    .tp-tracking-steps {
        display: flex;
        justify-content: space-between;
    }

    .tp-tracking-step {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 2px;
        flex: 1;
    }

    .tp-step-icon {
        font-size: 13px;
    }

    .tp-step-label {
        font-size: 8px;
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: .4px;
    }

    .tp-step-time {
        font-size: 8px;
    }

    .tp-step-done .tp-step-label,
    .tp-step-done .tp-step-time {
        color: #60a5fa;
    }

    .tp-step-pending {
        opacity: .38;
    }

    .tp-step-pending .tp-step-label,
    .tp-step-pending .tp-step-time {
        color: #9ca3af;
    }
</style>
```

- [ ] **Step 3: Verificar que compila**

```bash
dotnet build Login/Login.csproj
```

Esperado: `Build succeeded` sin errores.

- [ ] **Step 4: Correr la app y verificar visualmente**

```bash
dotnet run --project Login/Login.csproj
```

1. Abrir `http://localhost:5008`
2. Login con `cliente@local.com / Admin123`
3. Ir a Ordenes — las órdenes en estado `Asignada`, `Recolectada` o `Entregada` deben mostrar la barra de progreso con los hitos y horas correctas.
4. Las órdenes en estado `Creada` no deben mostrar el tracker.

- [ ] **Step 5: Commit**

```bash
git add Login/ViewModels/Tienda/OrdenTiendaRowVm.cs Login/Controllers/TiendaController.cs Login/Views/Tienda/Ordenes.cshtml
git commit -m "feat: agregar tracking de progreso en tarjetas de orden (tienda)"
```
