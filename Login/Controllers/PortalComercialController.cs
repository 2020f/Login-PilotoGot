using System.Security.Claims;
using System.Text.Json.Nodes;
using Envirapido.Portal.Contracts;
using Login.Application.Portal;
using Login.ViewModels.SuperAdmin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Login.Controllers;

[Authorize(Roles = "SuperAdmin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[Route("SuperAdmin/Portal")]
public sealed class PortalComercialController(PortalAdminClient portal, IOptions<PortalIntegrationOptions> options, TimeProvider clock) : Controller
{
    private string Actor => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
    private static string SafeTab(string? tab) => PortalAdminVm.Tabs.Contains(tab) ? tab! : "Solicitudes";

    [HttpGet("")]
    public async Task<IActionResult> Index(string tab = "Solicitudes", Guid? requestId = null,
        string q = "", string status = "", int page = 1, CancellationToken ct = default)
    {
        var model = new PortalAdminVm { Tab = SafeTab(tab), Search = q, Status = status, Configured = portal.Configured,
            Ok = TempData["PortalOk"] as string, Error = TempData["PortalError"] as string,
            Today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromHours(-4)).DateTime) };
        if (!ModelState.IsValid) model.Error = "El filtro o identificador no es válido.";
        if (!model.Configured) return View("~/Views/SuperAdmin/Portal.cshtml", model);
        var query = $"?q={Uri.EscapeDataString(q ?? "")}&status={Uri.EscapeDataString(status ?? "")}&page={Math.Max(1, page)}";
        try
        {
            model.Summary = await portal.GetAsync<PortalAdminSummary>("summary", Actor, ct);
            if (requestId.HasValue)
            {
                model.Detail = await portal.GetAsync<PortalAdminRequestDetail>($"requests/{requestId:D}", Actor, ct);
                model.Tab = "Solicitudes";
            }
            else switch (model.Tab)
            {
                case "Compradores": model.Users = await portal.GetAsync<PortalPage<PortalAdminUserDto>>("users" + query, Actor, ct); break;
                case "Catálogo web": model.Catalog = (await portal.GetAsync<PortalCatalogAdminResult>("catalog", Actor, ct)).Plans; break;
                case "Versiones Android": model.Artifacts = await portal.GetAsync<PortalArtifactListDto>("artifacts", Actor, ct); break;
                case "Auditoría": model.Audit = await portal.GetAsync<PortalPage<PortalAuditDto>>($"audit?page={Math.Max(1,page)}", Actor, ct); break;
                default: model.Requests = await portal.GetAsync<PortalPage<PortalAdminRequestDto>>("requests" + query, Actor, ct); break;
            }
        }
        catch (PortalBridgeException e) { model.Error = e.Message; }
        return View("~/Views/SuperAdmin/Portal.cshtml", model);
    }

    private async Task<IActionResult> Change(string path, object input, Guid operationId, string success,
        string tab = "Solicitudes", Guid? requestId = null, CancellationToken ct = default)
    {
        if (!ModelState.IsValid) TempData["PortalError"] = "Revisa los campos del formulario. No se enviaron cambios al portal.";
        else try
        {
            await portal.PostAsync<JsonObject>(path, Actor, input, operationId, ct);
            TempData["PortalOk"] = success;
        }
        catch (PortalBridgeException e) { TempData["PortalError"] = e.Message; }
        return RedirectToAction(nameof(Index), new { tab = SafeTab(tab), requestId });
    }

    [HttpPost("EstadoComprador"), ValidateAntiForgeryToken]
    public Task<IActionResult> UserStatus(Guid id, string status, string reason, string version, Guid operationId, CancellationToken ct)
        => Change($"users/{id:D}/status", new PortalAdminUserStatusInput(status, reason, version), operationId,
            "Estado de la cuenta web actualizado. El acceso CRM no cambió.", "Compradores", ct: ct);

    [HttpPost("Decision"), ValidateAntiForgeryToken]
    public Task<IActionResult> Decision(Guid requestId, string decision, string message, string version, Guid operationId, CancellationToken ct)
        => Change($"requests/{requestId:D}/decision", new PortalDecisionInput(decision, message, version), operationId,
            "Decisión registrada. Pago y vigencia se verifican por separado.", requestId: requestId, ct: ct);

    [HttpPost("ConfirmarPago"), ValidateAntiForgeryToken]
    public Task<IActionResult> ConfirmPayment(Guid requestId, decimal amount, DateOnly paidOn, string method,
        string reference, string reason, string version, bool confirmed, Guid operationId, Guid? reportId, CancellationToken ct)
    {
        if (!confirmed) ModelState.AddModelError("confirmed", "Confirma que el pago fue recibido fuera del sistema.");
        return Change("payments/confirm", new PortalPaymentConfirmInput(requestId, amount, paidOn, method, reference, reason, version, reportId),
            operationId, "Pago externo confirmado en el portal. No se registró ningún cobro en el CRM.", requestId: requestId, ct: ct);
    }

    [HttpPost("RevisarComprobante"), ValidateAntiForgeryToken]
    public Task<IActionResult> ReportDecision(Guid requestId, Guid reportId, string decision, string reason,
        string version, Guid operationId, CancellationToken ct)
        => Change($"payment-reports/{reportId:D}/decision", new PortalPaymentReportDecisionInput(decision, reason, version),
            operationId, "Revisión del comprobante registrada. Esto no confirma un pago.", requestId: requestId, ct: ct);

    [HttpPost("AnularPago"), ValidateAntiForgeryToken]
    public Task<IActionResult> VoidPayment(Guid requestId, Guid paymentId, string reason, string version,
        Guid operationId, CancellationToken ct)
        => Change($"payments/{paymentId:D}/void", new PortalPaymentVoidInput(reason, version), operationId,
            "Pago del portal anulado con trazabilidad.", requestId: requestId, ct: ct);

    [HttpPost("Vigencia"), ValidateAntiForgeryToken]
    public Task<IActionResult> Access(Guid requestId, Guid userId, DateOnly startsOn, DateOnly endsOn,
        string status, string reason, string? version, Guid operationId, CancellationToken ct)
        => Change("access", new PortalAccessInput(userId, requestId, startsOn, endsOn, status, reason, version), operationId,
            "Vigencia del portal actualizada. Las fechas y permisos CRM no cambiaron.", requestId: requestId, ct: ct);

    [HttpPost("PublicarPlan"), ValidateAntiForgeryToken]
    public Task<IActionResult> Catalog(string code, bool isPublished, string description, int sortOrder,
        bool featured, string? version, Guid operationId, CancellationToken ct)
        => Change("catalog", new PortalCatalogPublicationInput(code, isPublished, description, sortOrder, featured, version), operationId,
            "Publicación comercial actualizada. El precio y los límites conservan su fuente en Planes.", "Catálogo web", ct: ct);

    [HttpPost("SubirAPK"), ValidateAntiForgeryToken]
    [RequestSizeLimit(150L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 150L * 1024 * 1024)]
    public async Task<IActionResult> Upload(IFormFile? file, string? notes, string sourceCommit, string publicBaseUrl,
        Guid operationId, CancellationToken ct)
    {
        try
        {
            if (!ModelState.IsValid || file is null || file.Length == 0 || file.Length > options.Value.MaxUploadBytes
                || !string.Equals(Path.GetExtension(file.FileName), ".apk", StringComparison.OrdinalIgnoreCase))
                throw new PortalBridgeException("Selecciona un APK válido dentro del tamaño permitido.", 400);
            await using var content = file.OpenReadStream();
            await portal.UploadAsync<JsonObject>(content, file.FileName, notes ?? "", sourceCommit, publicBaseUrl, Actor, operationId, ct);
            TempData["PortalOk"] = "APK recibido en cuarentena. Valídalo y revisa sus datos antes de publicarlo.";
        }
        catch (PortalBridgeException e) { TempData["PortalError"] = e.Message; }
        return RedirectToAction(nameof(Index), new { tab = "Versiones Android" });
    }

    [HttpPost("ValidarAPK"), ValidateAntiForgeryToken]
    public Task<IActionResult> ValidateArtifact(Guid id, Guid operationId, CancellationToken ct)
        => Change($"artifacts/{id:D}/validate", new { }, operationId, "Validación del APK terminada; revisa el resultado.", "Versiones Android", ct: ct);

    [HttpPost("PublicarAPK"), ValidateAntiForgeryToken]
    public Task<IActionResult> PublishArtifact(Guid id, string expectedVersion, bool qaApproved, string reason,
        Guid operationId, CancellationToken ct)
    {
        if (!qaApproved) ModelState.AddModelError("qaApproved", "Confirma la QA de esta versión antes de publicarla.");
        return Change($"artifacts/{id:D}/publish", new { operationId, expectedVersion, qaApproved, reason }, operationId,
            "Versión Android publicada para los compradores con permiso y vigencia.", "Versiones Android", ct: ct);
    }

    [HttpPost("RetirarAPK"), ValidateAntiForgeryToken]
    public Task<IActionResult> WithdrawArtifact(Guid id, string expectedVersion, string reason,
        Guid operationId, CancellationToken ct)
        => Change($"artifacts/{id:D}/withdraw", new { operationId, expectedVersion, reason }, operationId,
            "Versión retirada de las descargas nuevas; las apps instaladas no se modificaron.", "Versiones Android", ct: ct);
}
