using System.Text.RegularExpressions;

namespace Envirapido.Portal.Api.Artifacts;

public sealed class PortalArtifactOptions
{
    public const string PackageName = "com.pilotgo.app";
    public const string OriginalCertificate = "773d72ba127d16a95f032d520d37162708ea8676bf2f7e3e48168f751724e487";
    public string StoragePath { get; set; } = "";
    public long MaxUploadBytes { get; set; } = 150L * 1024 * 1024;
    public long MaxExpandedBytes { get; set; } = 1024L * 1024 * 1024;
    public int MinimumVersionCode { get; set; } = 9;
    public string JavaPath { get; set; } = "";
    public string ApkSignerJarPath { get; set; } = "";
    public string Aapt2Path { get; set; } = "";
    public string[] AllowedCertificateSha256 { get; set; } = [OriginalCertificate];
    public int ToolTimeoutSeconds { get; set; } = 30;

    public long UploadLimit => Math.Clamp(MaxUploadBytes, 1024 * 1024, 512L * 1024 * 1024);
    public long ExpandedLimit => Math.Clamp(MaxExpandedBytes, UploadLimit, 2L * 1024 * 1024 * 1024);
    public int EffectiveMinimumVersionCode(IHostEnvironment environment) =>
        environment.IsDevelopment() || environment.IsEnvironment("Testing")
            ? Math.Max(0, MinimumVersionCode) : Math.Max(9, MinimumVersionCode);

    public bool IsCertificateAllowed(string digest) => AllowedCertificateSha256.Length > 0
        && AllowedCertificateSha256.All(x => Regex.IsMatch(x, "^[a-fA-F0-9]{64}$"))
        && AllowedCertificateSha256.Contains(digest, StringComparer.OrdinalIgnoreCase);
}

public sealed class PortalArtifactException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
