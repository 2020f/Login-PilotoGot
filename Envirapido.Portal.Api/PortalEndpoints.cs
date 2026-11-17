using Envirapido.Portal.Api.Security;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace Envirapido.Portal.Api;

public static class PortalEndpoints
{
    public static void MapPortalCore(this WebApplication app)
    {
        var v1 = app.MapGroup("/portal-api/v1");
        v1.MapGet("/csrf", (HttpContext c, IAntiforgery anti) => Results.Ok(new PortalCsrf(anti.GetAndStoreTokens(c).RequestToken!)));
        v1.MapGet("/catalog", async (PortalCatalogService catalog, CancellationToken ct) => Results.Ok(await catalog.PublicAsync(ct)));
        var auth = v1.MapGroup("/auth").RequireRateLimiting("PortalAuth");
        auth.MapPost("/register", async (HttpContext c, PortalRegisterInput input, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); var id = await svc.RegisterAsync(input, PortalSecurity.OperationKey(c), ct); await svc.SignInAsync(c, id, false, ct); return Results.Json(await svc.AccountAsync(id, ct), statusCode: 201); });
        auth.MapPost("/login", async (HttpContext c, PortalLoginInput input, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); var user = await svc.LoginAsync(input, ct); await svc.SignInAsync(c, user.Id, input.RememberMe, ct); return Results.Ok(await svc.AccountAsync(user.Id, ct)); });
        auth.MapPost("/logout", async (HttpContext c, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); await svc.LogoutAsync(c, ct); return Results.NoContent(); }).RequireAuthorization("PortalBuyer");
        auth.MapGet("/me", async (HttpContext c, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.AccountAsync(PortalSecurity.UserId(c), ct))).RequireAuthorization("PortalBuyer");
        auth.MapPost("/confirm-email", async (HttpContext c, PortalTokenInput input, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); await svc.ConfirmEmailAsync(input, ct); return Results.Ok(new PortalMessage("Correo del portal confirmado.")); });
        auth.MapPost("/resend-confirmation", async (HttpContext c, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); await svc.ResendConfirmationAsync(PortalSecurity.UserId(c), ct); return Results.Ok(new PortalMessage("Si corresponde, recibirás un correo de confirmación.")); }).RequireAuthorization("PortalBuyer");
        auth.MapPost("/forgot-password", async (HttpContext c, PortalEmailInput input, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); await svc.ForgotPasswordAsync(input, ct); return Results.Ok(new PortalMessage("Si existe una cuenta, recibirás instrucciones para recuperar tu acceso.")); });
        auth.MapPost("/reset-password", async (HttpContext c, PortalResetInput input, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); await svc.ResetPasswordAsync(input, ct); return Results.Ok(new PortalMessage("Contraseña del portal actualizada. Inicia sesión nuevamente.")); });
        var buyer = app.MapGroup("/portal-api/v1").RequireAuthorization("PortalBuyer");
        buyer.MapGet("/account", async (HttpContext c, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.AccountAsync(PortalSecurity.UserId(c), ct)));
        buyer.MapPut("/account", async (HttpContext c, PortalProfileInput input, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); return Results.Ok(await svc.UpdateProfileAsync(PortalSecurity.UserId(c), input, ct)); });
        buyer.MapGet("/requests", async (HttpContext c, PortalDomainService svc, CancellationToken ct) => Results.Ok(new PortalRequestsDto(await svc.RequestsAsync(PortalSecurity.UserId(c), ct))));
        buyer.MapPost("/requests", async (HttpContext c, PortalRequestInput input, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); return Results.Json(await svc.SubmitRequestAsync(PortalSecurity.UserId(c), input, PortalSecurity.OperationKey(c), ct), statusCode: 201); });
        buyer.MapPost("/requests/{id:guid}/payment-reports", async (HttpContext c, Guid id, PortalPaymentReportInput input, PortalDomainService svc, CancellationToken ct) =>
        { await PortalSecurity.ValidateCsrfAsync(c); return Results.Json(await svc.ReportPaymentAsync(PortalSecurity.UserId(c), id, input, PortalSecurity.OperationKey(c), ct), statusCode: 201); });
        buyer.MapGet("/subscription", async (HttpContext c, PortalDomainService svc, PortalAccessService access, CancellationToken ct) =>
        { var id = PortalSecurity.UserId(c); return Results.Ok(new PortalSubscriptionDto(await access.GetAccessAsync(id, ct), await svc.RequestsAsync(id, ct))); });
        var admin = app.MapGroup("/portal-api/internal/admin").RequireAuthorization(PortalSecurity.AdminPolicy);
        admin.MapGet("/summary", async (PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.SummaryAsync(ct)));
        admin.MapGet("/users", async (string? q, string? status, int? page, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.AdminUsersAsync(q, status, page ?? 1, ct)));
        admin.MapPost("/users/{id:guid}/status", async (Guid id, PortalAdminUserStatusInput input, HttpContext c, PortalDomainService svc, CancellationToken ct) =>
            Results.Ok(await svc.SetUserStatusAsync(id, input, PortalSecurity.Actor(c), PortalSecurity.OperationKey(c), ct)));
        admin.MapGet("/requests", async (string? q, string? status, int? page, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.AdminRequestsAsync(q, status, page ?? 1, ct)));
        admin.MapGet("/requests/{id:guid}", async (Guid id, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.AdminDetailAsync(id, ct)));
        admin.MapPost("/requests/{id:guid}/decision", async (Guid id, PortalDecisionInput input, HttpContext c, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.DecideAsync(id, input, PortalSecurity.Actor(c), PortalSecurity.OperationKey(c), ct)));
        admin.MapPost("/payments/confirm", async (PortalPaymentConfirmInput input, HttpContext c, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.ConfirmPaymentAsync(input, PortalSecurity.Actor(c), PortalSecurity.OperationKey(c), ct)));
        admin.MapPost("/payment-reports/{id:guid}/decision", async (Guid id, PortalPaymentReportDecisionInput input, HttpContext c, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.DecidePaymentReportAsync(id, input, PortalSecurity.Actor(c), PortalSecurity.OperationKey(c), ct)));
        admin.MapPost("/payments/{id:guid}/void", async (Guid id, PortalPaymentVoidInput input, HttpContext c, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.VoidPaymentAsync(id, input, PortalSecurity.Actor(c), PortalSecurity.OperationKey(c), ct)));
        admin.MapPost("/access", async (PortalAccessInput input, HttpContext c, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.SetAccessAsync(input, PortalSecurity.Actor(c), PortalSecurity.OperationKey(c), ct)));
        admin.MapGet("/catalog", async (PortalCatalogService svc, CancellationToken ct) => Results.Ok(new { plans = await svc.AdminAsync(ct) }));
        admin.MapPost("/catalog", async (PortalCatalogPublicationInput input, HttpContext c, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.PublishCatalogAsync(input, PortalSecurity.Actor(c), PortalSecurity.OperationKey(c), ct)));
        admin.MapGet("/audit", async (int? page, PortalDomainService svc, CancellationToken ct) => Results.Ok(await svc.AuditAsync(page ?? 1, ct)));
        app.MapGet("/portal-api/health", () => Results.Ok(new { service = "Envirapido.Portal", status = "running" }));
    }
}
