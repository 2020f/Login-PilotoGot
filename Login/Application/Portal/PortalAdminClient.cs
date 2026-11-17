using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace Login.Application.Portal;

public sealed class PortalBridgeException(string message, int status = 503) : Exception(message)
{
    public int Status { get; } = status;
}

// El navegador nunca recibe la clave interna ni elige un destino HTTP.
// No usa ApplicationDbContext: las escrituras pertenecen al servicio Portal.
public sealed class PortalAdminClient(HttpClient http, IOptions<PortalIntegrationOptions> options)
{
    public bool Configured => options.Value.HasAdminConnection;
    private Uri Address(string relative)
    {
        if (!Configured) throw new PortalBridgeException("Configura la conexión privada del portal para administrar sus solicitudes.");
        return new Uri(new Uri(options.Value.AdminBaseUrl.TrimEnd('/') + "/"),
            "portal-api/internal/admin/" + relative.TrimStart('/'));
    }

    public async Task<T> GetAsync<T>(string path, string actor, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Get, path, actor, null, null, ct);

    public async Task<T> PostAsync<T>(string path, string actor, object body, Guid operationId, CancellationToken ct = default)
        => await SendAsync<T>(HttpMethod.Post, path, actor, JsonContent.Create(body), operationId, ct);

    public async Task<T> UploadAsync<T>(Stream file, string fileName, string notes, string sourceCommit,
        string publicBaseUrl, string actor, Guid operationId, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(file), "file", Path.GetFileName(fileName));
        form.Add(new StringContent(notes ?? ""), "notes");
        form.Add(new StringContent(sourceCommit ?? ""), "sourceCommit");
        form.Add(new StringContent(publicBaseUrl ?? ""), "publicBaseUrl");
        return await SendAsync<T>(HttpMethod.Post, "artifacts/upload", actor, form, operationId, ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, string actor, HttpContent? content,
        Guid? operationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actor)) throw new PortalBridgeException("No se identificó al administrador.", 403);
        if (method != HttpMethod.Get && (!operationId.HasValue || operationId == Guid.Empty))
            throw new PortalBridgeException("Recarga el formulario para obtener una clave de operación válida.", 400);
        using var request = new HttpRequestMessage(method, Address(path));
        // Salto servidor-servidor privado: el backend confía únicamente en su
        // proxy loopback. Conserva HTTPS en el enrutamiento público sin seguir
        // redirecciones ni permitir HTTP hacia un equipo remoto.
        if (request.RequestUri!.Scheme == Uri.UriSchemeHttp && request.RequestUri.IsLoopback)
            request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Envirapido-Portal-Key", options.Value.AdminKey);
        request.Headers.Add("X-Envirapido-Operator", actor);
        if (operationId.HasValue) request.Headers.Add("Idempotency-Key", operationId.Value.ToString());
        request.Content = content;
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                var message = status >= 500 ? "El portal no está disponible. Consulta el estado antes de repetir una operación."
                    : status is 401 or 403 ? "La conexión privada del portal no autorizó esta operación."
                    : "No se pudo completar la operación del portal.";
                if (status is 400 or 409 or 422)
                {
                    try
                    {
                        var error = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: ct);
                        var detail = error?["error"]?.GetValue<string>();
                        if (!string.IsNullOrWhiteSpace(detail)) message = detail.Length > 400 ? detail[..400] : detail;
                    }
                    catch (JsonException) { }
                }
                throw new PortalBridgeException(message, status is >= 300 and < 400 ? 503 : status);
            }
            var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
            return result ?? throw new PortalBridgeException("El portal devolvió una respuesta incompleta.");
        }
        catch (HttpRequestException) { throw new PortalBridgeException("No se pudo conectar con el portal. La operación del CRM sigue disponible."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        { throw new PortalBridgeException("El portal tardó en responder. Consulta el historial antes de repetir el envío."); }
        catch (JsonException) { throw new PortalBridgeException("La respuesta del portal no tiene el formato esperado."); }
    }
}
