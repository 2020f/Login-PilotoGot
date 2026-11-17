namespace Login.Application.Portal;

public sealed class PortalIntegrationOptions
{
    public const string Section = "PortalIntegration";
    public bool Enabled { get; set; }
    public string AdminBaseUrl { get; set; } = "";
    public string AdminKey { get; set; } = "";
    public string CatalogReadKey { get; set; } = "";
    public string CatalogCurrency { get; set; } = "DOP";
    public long MaxUploadBytes { get; set; } = 120L * 1024 * 1024;

    public static bool StrongKey(string value) => value.Length >= 32
        && !value.Contains("ChangeMe", StringComparison.OrdinalIgnoreCase)
        && !value.Contains("REPLACE", StringComparison.OrdinalIgnoreCase);

    public bool HasAdminConnection => Enabled && StrongKey(AdminKey)
        && Uri.TryCreate(AdminBaseUrl, UriKind.Absolute, out var uri)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && (uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback);
}
