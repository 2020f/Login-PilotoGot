using System.Text;
using Envirapido.Portal.Api.Artifacts;
using Envirapido.Portal.Api.Security;
using Envirapido.Portal.Api.Services;
using Envirapido.Portal.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Envirapido.Portal.Api;

public static class PortalArtifactEndpoints
{
    public static IServiceCollection AddPortalArtifacts(this IServiceCollection services,
        IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddOptions<PortalArtifactOptions>().Bind(configuration.GetSection("PortalArtifacts"));
        services.TryAddSingleton<PortalArtifactStorage>();
        services.TryAddSingleton<IApkInspector, OfficialAndroidApkInspector>();
        services.AddScoped<PortalArtifactService>();
        return services;
    }

    public static WebApplication MapPortalArtifacts(this WebApplication app)
    {
        var admin = app.MapGroup("/portal-api/internal/admin/artifacts").RequireAuthorization(PortalSecurity.AdminPolicy);
        admin.MapGet("", (HttpContext context, PortalArtifactService service, CancellationToken ct) =>
            Guard(context, async () => Results.Ok(await service.ListAsync(ct))));
        admin.MapGet("/{id:guid}", (Guid id, HttpContext context, PortalArtifactService service, CancellationToken ct) =>
            Guard(context, async () => Results.Ok(await service.GetAsync(id, ct))));
        admin.MapPost("/upload", (HttpContext context, PortalArtifactStorage storage,
                PortalArtifactService service, IOptionsMonitor<PortalArtifactOptions> settings, CancellationToken ct) =>
            Guard(context, async () =>
            {
                var operation = PortalSecurity.OperationKey(context);
                var received = await ReadUploadAsync(context, storage, settings.CurrentValue, ct);
                try
                {
                    var dto = await service.UploadAsync(received.File, received.Name,
                        received.Fields.GetValueOrDefault("notes", ""), received.Fields.GetValueOrDefault("sourceCommit", ""),
                        received.Fields.GetValueOrDefault("publicBaseUrl", ""), received.Fields.GetValueOrDefault("expectedSha256"),
                        PortalSecurity.Actor(context), operation, ct);
                    return Results.Created($"/portal-api/internal/admin/artifacts/{dto.Id}", dto);
                }
                finally { storage.DeleteTemporary(received.File.TemporaryPath); }
            }));
        admin.MapPost("/{id:guid}/validate", (Guid id, HttpContext context, PortalArtifactService service, CancellationToken ct) =>
            Guard(context, async () =>
            {
                _ = PortalSecurity.OperationKey(context);
                return Results.Ok(await service.ValidateAsync(id, PortalSecurity.Actor(context), ct));
            }));
        admin.MapPost("/{id:guid}/publish", (Guid id, PortalArtifactPublishRequest request, HttpContext context,
                PortalArtifactService service, CancellationToken ct) =>
            Guard(context, async () =>
            {
                _ = PortalSecurity.OperationKey(context);
                return Results.Ok(await service.PublishAsync(id, request, PortalSecurity.Actor(context), ct));
            }));
        admin.MapPost("/{id:guid}/withdraw", (Guid id, PortalArtifactWithdrawRequest request, HttpContext context,
                PortalArtifactService service, CancellationToken ct) =>
            Guard(context, async () =>
            {
                _ = PortalSecurity.OperationKey(context);
                return Results.Ok(await service.WithdrawAsync(id, request, PortalSecurity.Actor(context), ct));
            }));

        var downloads = app.MapGroup("/portal-api/v1/downloads")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = PortalSecurity.CookieScheme });
        downloads.MapGet("/current", (HttpContext context, PortalAccessService access,
                PortalArtifactService service, CancellationToken ct) =>
            Guard(context, async () =>
            {
                await RequireAccessAsync(context, access, ct);
                return Results.Ok(await service.CurrentDownloadAsync(ct));
            }));
        downloads.MapMethods("/{id:guid}/file", ["GET", "HEAD"], (Guid id, HttpContext context,
                PortalAccessService access, PortalArtifactService service, CancellationToken ct) =>
            Guard(context, async () =>
            {
                // Always execute before file conditions/ranges. A previous valid
                // download URL never bypasses suspension or the portal window.
                var userId = await RequireAccessAsync(context, access, ct);
                var file = await service.OpenDownloadAsync(id, userId, context.Request.Headers.ContainsKey(HeaderNames.Range), ct);
                context.Response.Headers.CacheControl = "private, no-store";
                context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
                context.Response.Headers["Content-Security-Policy"] = "sandbox";
                return Results.File(file.Stream, "application/vnd.android.package-archive", file.FileName,
                    file.PublishedAt, new EntityTagHeaderValue('"' + file.Sha256 + '"'), enableRangeProcessing: true);
            }));
        return app;
    }

    private static async Task<Guid> RequireAccessAsync(HttpContext context, PortalAccessService service, CancellationToken ct)
    {
        var userId = PortalSecurity.UserId(context);
        var access = await service.GetAccessAsync(userId, ct);
        if (!access.Allowed) throw new PortalArtifactException(403, access.Reason switch
        {
            "EMAIL_UNCONFIRMED" => "Confirma tu correo antes de descargar la aplicación.",
            "EXPIRED" => "Tu vigencia del portal terminó. Solicita una renovación.",
            "NOT_STARTED" => "Tu período de acceso todavía no ha comenzado.",
            "PAYMENT_PENDING" => "Administración debe confirmar el pago antes de habilitar la descarga.",
            "ACCOUNT_SUSPENDED" or "SUSPENDED" or "REVOKED" => "Tu acceso al portal está suspendido o revocado. Contacta con administración.",
            _ => "Tu solicitud y su vigencia deben estar aprobadas antes de descargar la aplicación."
        });
        return userId;
    }

    private static async Task<IResult> Guard(HttpContext context, Func<Task<IResult>> action)
    {
        context.Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (PortalArtifactException failure)
        { return Results.Json(new PortalError(failure.Message, "artifact_request_rejected"), statusCode: failure.StatusCode); }
        catch (InvalidDataException)
        { return Results.Json(new PortalError("El formulario o archivo no es válido.", "artifact_invalid_form"), statusCode: 400); }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PortalArtifacts")
                .LogError(failure, "No se pudo acceder al almacenamiento de versiones. TraceId {TraceId}", context.TraceIdentifier);
            return Results.Json(new PortalError("El almacenamiento privado no está disponible temporalmente.", "artifact_storage_unavailable"), statusCode: 503);
        }
    }

    private sealed record Upload(ReceivedArtifact File, string Name, Dictionary<string, string> Fields);

    private static async Task<Upload> ReadUploadAsync(HttpContext context, PortalArtifactStorage storage,
        PortalArtifactOptions settings, CancellationToken ct)
    {
        if (!storage.IsConfigured) throw new PortalArtifactException(503, "El almacenamiento privado de versiones no está configurado.");
        var maxBody = settings.UploadLimit + 64 * 1024;
        if (context.Request.ContentLength > maxBody) throw new PortalArtifactException(413, "El APK supera el tamaño máximo permitido.");
        var requestLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (requestLimit is { IsReadOnly: false }) requestLimit.MaxRequestBodySize = maxBody;
        if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType)
            || !contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new PortalArtifactException(400, "Envía un formulario multipart con un único archivo APK.");
        var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
        if (string.IsNullOrEmpty(boundary) || boundary.Length > 128)
            throw new PortalArtifactException(400, "El formulario multipart no es válido.");
        var reader = new MultipartReader(boundary, context.Request.Body)
            { BodyLengthLimit = settings.UploadLimit + 1, HeadersCountLimit = 16, HeadersLengthLimit = 8 * 1024 };
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        ReceivedArtifact? received = null;
        var name = "";
        try
        {
            var count = 0;
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
            {
                if (++count > 5 || !ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
                    || !disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
                    throw new PortalArtifactException(400, "El formulario de versión no es válido.");
                var key = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? "";
                if (disposition.FileName.HasValue || disposition.FileNameStar.HasValue)
                {
                    if (key != "file" || received is not null) throw new PortalArtifactException(400, "Adjunta un único archivo en el campo file.");
                    name = HeaderUtilities.RemoveQuotes(disposition.FileNameStar.HasValue ? disposition.FileNameStar : disposition.FileName).Value ?? "";
                    if (!Path.GetExtension(name).Equals(".apk", StringComparison.OrdinalIgnoreCase))
                        throw new PortalArtifactException(400, "Solo se admiten archivos APK Android.");
                    received = await storage.ReceiveAsync(section.Body, ct);
                }
                else
                {
                    if (key is not ("notes" or "sourceCommit" or "publicBaseUrl" or "expectedSha256") || fields.ContainsKey(key))
                        throw new PortalArtifactException(400, "El formulario contiene campos no permitidos o repetidos.");
                    fields[key] = await ReadSmallFieldAsync(section.Body, ct);
                }
            }
            if (received is null) throw new PortalArtifactException(400, "Adjunta el archivo APK release.");
            return new(received, name, fields);
        }
        catch { if (received is not null) storage.DeleteTemporary(received.TemporaryPath); throw; }
    }

    private static async Task<string> ReadSmallFieldAsync(Stream stream, CancellationToken ct)
    {
        using var output = new MemoryStream();
        var buffer = new byte[1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + read > 16 * 1024) throw new PortalArtifactException(400, "Un campo del formulario supera el tamaño permitido.");
            output.Write(buffer, 0, read);
        }
        try { return new UTF8Encoding(false, true).GetString(output.ToArray()); }
        catch (DecoderFallbackException) { throw new PortalArtifactException(400, "El texto del formulario no tiene una codificación válida."); }
    }
}
