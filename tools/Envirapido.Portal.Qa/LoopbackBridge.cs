using System.Net;
using System.Text.RegularExpressions;
using Login.Tests;

namespace Envirapido.Portal.Qa;

// Adaptador de transporte exclusivo del harness. El producto permanece en TestServer;
// los navegadores y el otro servicio lo alcanzan por un puerto que solo escucha loopback.
internal static class LoopbackBridge
{
    private const long MaxBody = 151L * 1024 * 1024;
    private static readonly HashSet<string> SkipHeaders = new(StringComparer.OrdinalIgnoreCase)
        { "Host", "Connection", "Transfer-Encoding", "Expect", "Forwarded", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-For" };

    public static WebApplication Create(string origin, string spoolRoot, HttpClient client,
        Func<HttpContext, Task<bool>> localEndpoints)
    {
        Directory.CreateDirectory(spoolRoot);
        var builder = WebApplication.CreateBuilder(Array.Empty<string>());
        builder.WebHost.UseUrls(origin).ConfigureKestrel(k => k.Limits.MaxRequestBodySize = MaxBody);
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var uploadSlot = new SemaphoreSlim(1, 1);
        app.Run(async context =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)
                || !Uri.TryCreate("http://" + context.Request.Host, UriKind.Absolute, out var host) || !host.IsLoopback)
            { context.Response.StatusCode = 403; return; }
            if (await localEndpoints(context)) return;
            // Las utilidades QA no se proxifican a controladores del producto.
            if (context.Request.Path.StartsWithSegments("/qa")) { context.Response.StatusCode = 404; return; }
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            FileStream? spool = null;
            var ownsUploadSlot = false;
            try
            {
                // Solo las dos rutas de carga conocidas pueden reservar espacio para un APK.
                var isUpload = HttpMethods.IsPost(context.Request.Method) && (context.Request.Path == "/SuperAdmin/Portal/SubirAPK"
                    || context.Request.Path == "/portal-api/internal/admin/artifacts/upload");
                var bodyLimit = isUpload ? MaxBody : 1024 * 1024;
                if (context.Request.ContentLength > bodyLimit) { context.Response.StatusCode = 413; return; }
                if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
                {
                    if (isUpload) { await uploadSlot.WaitAsync(context.RequestAborted); ownsUploadSlot = true; }
                    // No lectura tardía del stream Kestrel desde TestServer ni APK completo en RAM.
                    var path = Path.Combine(spoolRoot, Guid.NewGuid().ToString("N") + ".request");
                    spool = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536,
                        FileOptions.Asynchronous | FileOptions.DeleteOnClose);
                    var buffer = new byte[65536]; int count;
                    while ((count = await context.Request.Body.ReadAsync(buffer, context.RequestAborted)) > 0)
                    {
                        if (spool.Length + count > bodyLimit) { context.Response.StatusCode = 413; return; }
                        await spool.WriteAsync(buffer.AsMemory(0, count), context.RequestAborted);
                    }
                    spool.Position = 0; request.Content = new StreamContent(spool);
                }
                foreach (var header in context.Request.Headers)
                    if (!SkipHeaders.Contains(header.Key) && !request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                        request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                request.Headers.Host = new Uri(origin).Authority;
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
                context.Response.StatusCode = (int)response.StatusCode;
                foreach (var header in response.Headers.Concat(response.Content.Headers))
                    if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)
                        && !header.Key.Equals("Connection", StringComparison.OrdinalIgnoreCase)) context.Response.Headers[header.Key] = header.Value.ToArray();
                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            finally
            {
                if (spool is not null) await spool.DisposeAsync();
                if (ownsUploadSlot) uploadSlot.Release();
            }
        });
        return app;
    }

    public static async Task LoginAsync(HttpContext context, HttpClient client, string id)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) { context.Response.StatusCode = 405; return; }
        using var login = await client.GetAsync("/Identity/Account/Login", context.RequestAborted);
        var html = await login.Content.ReadAsStringAsync(context.RequestAborted);
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!login.IsSuccessStatusCode || !token.Success || !login.Headers.TryGetValues("Set-Cookie", out var cookies))
            throw new InvalidOperationException("No se obtuvo CSRF de acceso CRM QA.");
        using var post = new HttpRequestMessage(HttpMethod.Post, "/Identity/Account/Login")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = id + "@example.test", ["Input.Password"] = PilotGoFactory.Password,
                ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value)
            })
        };
        post.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies.Select(c => c.Split(';')[0])));
        using var response = await client.SendAsync(post, context.RequestAborted);
        if (response.StatusCode != HttpStatusCode.Redirect || !response.Headers.TryGetValues("Set-Cookie", out var signedCookies))
            throw new InvalidOperationException("No se pudo autenticar la cuenta ficticia CRM QA.");
        foreach (var cookie in signedCookies) context.Response.Headers.Append("Set-Cookie", cookie);
        context.Response.Redirect(id == "qa-admin" ? "/SuperAdmin/Portal" : id == "manager-a" ? "/Operaciones" : id == "shop-a" ? "/Tienda/Ordenes" : "/Piloto");
    }
}
