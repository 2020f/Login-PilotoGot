using System.Text.RegularExpressions;

namespace Envirapido.Portal.Qa;

internal sealed record QaOptions(int CrmPort, int PortalPort, Uri WebOrigin, string ReceiptPath,
    string SqlInstance, string? AndroidTools, string? JavaPath, bool VerifyOnly)
{
    public string CrmOrigin => $"http://127.0.0.1:{CrmPort}";
    public string PortalOrigin => $"http://127.0.0.1:{PortalPort}";
    public static QaOptions Parse(string[] args)
    {
        var allowed = new HashSet<string> { "--crm-port", "--portal-port", "--web-origin", "--receipt", "--sql-instance", "--android-tools", "--java", "--verify-seed" };
        var values = new Dictionary<string, string>(); var verify = false;
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!allowed.Contains(key) || values.ContainsKey(key)) throw new ArgumentException("Argumento QA desconocido o duplicado.");
            if (key == "--verify-seed") { if (verify) throw new ArgumentException("Argumento duplicado."); verify = true; continue; }
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"{key} requiere un valor.");
            values.Add(key, args[i]);
        }
        int Port(string key, int fallback) => !values.TryGetValue(key, out var raw) ? fallback
            : int.TryParse(raw, out var port) && port is >= 1024 and <= 65535 ? port : throw new ArgumentException("Puerto QA inválido.");
        var crm = Port("--crm-port", 5098); var portal = Port("--portal-port", 5031);
        if (crm == portal) throw new ArgumentException("CRM y Portal necesitan puertos distintos.");
        if (!Uri.TryCreate(values.GetValueOrDefault("--web-origin", "http://127.0.0.1:3000"), UriKind.Absolute, out var web)
            || web.Scheme != "http" || !web.IsLoopback || web.AbsolutePath != "/" || web.UserInfo.Length != 0 || web.Query.Length != 0 || web.Fragment.Length != 0
            || web.Port == crm || web.Port == portal) throw new ArgumentException("El origen Next requiere HTTP loopback y puerto propio.");
        var instance = values.GetValueOrDefault("--sql-instance", Environment.GetEnvironmentVariable("PILOTGO_TEST_SQLSERVER_INSTANCE") ?? "PilotGoVisualQA_20261111");
        if (!Regex.IsMatch(instance, "^[A-Za-z0-9_-]{1,64}$")) throw new ArgumentException("Instancia QA inválida.");
        var receipt = Path.GetFullPath(values.GetValueOrDefault("--receipt", Path.Combine("artifacts", "portal-implementation-20261117", "qa-local-receipt.json")));
        var android = values.GetValueOrDefault("--android-tools"); var java = values.GetValueOrDefault("--java");
        if (android is not null && (!Path.IsPathFullyQualified(android) || !File.Exists(Path.Combine(android, "aapt2.exe")) || !File.Exists(Path.Combine(android, "lib", "apksigner.jar"))))
            throw new ArgumentException("--android-tools requiere el directorio absoluto oficial de Android build-tools con aapt2.exe y lib/apksigner.jar.");
        if (android is not null && (java is null || !Path.IsPathFullyQualified(java) || !File.Exists(java)))
            throw new ArgumentException("La validación APK requiere --java con la ruta absoluta de java.exe.");
        return new(crm, portal, web, receipt, instance, android, java, verify);
    }
}
