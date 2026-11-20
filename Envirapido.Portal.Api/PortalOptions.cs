using System.Net;
using Microsoft.Data.SqlClient;

namespace Envirapido.Portal.Api;

public sealed class PortalOptions
{
    public const string CrmLoginUrl = "https://envirapido.com/Identity/Account/Login";
    public string AdminKey { get; set; } = "";
    public string CatalogKey { get; set; } = "";
    public string CatalogUrl { get; set; } = "https://envirapido.com/api/internal/portal/catalog";
    public string PublicBaseUrl { get; set; } = "https://portal.envirapido.com";
    public string DataProtectionDirectory { get; set; } = "";
    public string TermsVersion { get; set; } = "2026-11-17";
    public string PrivacyVersion { get; set; } = "2026-11-17";
    // La cuenta web se registra sin correo transaccional; SuperAdmin activa
    // sus recursos mediante aprobación, pago aplicable y vigencia explícita.
    public bool RequireEmailConfirmation { get; set; }
    public PortalMailOptions Mail { get; set; } = new();

    public static bool Local(IHostEnvironment env) => env.IsDevelopment() || env.IsEnvironment("Testing");
    public void Validate(IHostEnvironment environment, string? connection)
    {
        if (Local(environment))
        {
            if (string.IsNullOrEmpty(AdminKey)) AdminKey = "Testing-Only-Portal-Admin-Key-Not-For-Production-2026";
            if (string.IsNullOrEmpty(CatalogKey)) CatalogKey = "Testing-Only-Portal-Catalog-Key-Not-For-Production-2026";
        }
        else
        {
            StrongSecret(AdminKey, "Portal:AdminKey"); StrongSecret(CatalogKey, "Portal:CatalogKey");
            if (AdminKey == CatalogKey) throw new InvalidOperationException("Las claves administrativas y de catálogo deben ser diferentes.");
            if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("Configura ConnectionStrings:PortalConnection; no se admite DefaultConnection.");
            if (!Path.IsPathFullyQualified(DataProtectionDirectory)) throw new InvalidOperationException("Portal:DataProtectionDirectory requiere un directorio privado absoluto.");
            var web = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "wwwroot")) + Path.DirectorySeparatorChar;
            if ((Path.GetFullPath(DataProtectionDirectory) + Path.DirectorySeparatorChar).StartsWith(web, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Las claves del portal deben permanecer fuera de wwwroot.");
        }
        ValidateUrl(PublicBaseUrl, Local(environment)); ValidateUrl(CatalogUrl, true);
        if (string.IsNullOrWhiteSpace(TermsVersion) || TermsVersion.Length > 80 || string.IsNullOrWhiteSpace(PrivacyVersion) || PrivacyVersion.Length > 80)
            throw new InvalidOperationException("Configura las versiones de políticas del portal.");
        if (!string.IsNullOrEmpty(connection))
        {
            var sql = new SqlConnectionStringBuilder(connection);
            if (!sql.InitialCatalog.StartsWith("EnvirapidoPortal", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PortalConnection debe apuntar a una base independiente con prefijo EnvirapidoPortal, nunca a la base CRM.");
        }
        if (Mail.Enabled && (string.IsNullOrWhiteSpace(Mail.Host) || string.IsNullOrWhiteSpace(Mail.FromAddress) || Mail.Port is < 1 or > 65535))
            throw new InvalidOperationException("Completa la configuración SMTP propia del portal.");
        if (Mail.Enabled && !Mail.UseSsl && !Local(environment)) throw new InvalidOperationException("El SMTP del portal requiere TLS fuera de Development/Testing.");
    }
    private static void StrongSecret(string value, string name)
    {
        if (value.Length is < 32 or > 512 || value.Any(char.IsWhiteSpace) || new[] { "Testing", "Example", "ChangeMe", "DevOnly", "REEMPLAZAR", "SECRETO", "MISMO_", "REPLACE" }.Any(x => value.Contains(x, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"{name} requiere un secreto privado fuerte de al menos 32 caracteres.");
    }
    private static void ValidateUrl(string value, bool allowHttpLoopback)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
            || !(uri.Scheme == "https" || allowHttpLoopback && uri.Scheme == "http" && uri.IsLoopback))
            throw new InvalidOperationException("La URL requiere HTTPS o una excepción de HTTP loopback autorizada para este enlace interno.");
    }
}
public sealed class PortalMailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Envirapido";
}
