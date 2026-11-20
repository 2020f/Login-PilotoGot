using System.IO.Compression;
using Envirapido.Portal.Api.Artifacts;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Envirapido.Portal.Tests;

public sealed class ArtifactsInspectionTests
{
    // Representative output obtained from the official SDK tools against the
    // locally distributed 2.0.1 (9) APK; no APK code is executed by these tests.
    private const string Signature = "Verifies\r\nNumber of signers: 1\r\nSigner #1 certificate DN: CN=PilotGo, OU=Mobile, O=PilotGo, L=Santo Domingo, C=DO\r\nSigner #1 certificate SHA-256 digest: " + PortalArtifactOptions.OriginalCertificate + "\r\n";
    private const string Badging = "package: name='com.pilotgo.app' versionCode='9' versionName='2.0.1' platformBuildVersionName='16'\r\nminSdkVersion:'24'\r\ntargetSdkVersion:'36'\r\nnative-code: 'arm64-v8a' 'armeabi-v7a' 'x86_64'\r\n";

    [Fact]
    public void Official_SDK_output_extracts_real_version_certificate_SDK_and_architectures()
    {
        var result = OfficialAndroidApkInspector.ParseOutputs(Signature, Badging, new());
        Assert.Equal("com.pilotgo.app", result.PackageName);
        Assert.Equal("2.0.1", result.VersionName);
        Assert.Equal(9, result.VersionCode);
        Assert.Equal(PortalArtifactOptions.OriginalCertificate, result.CertificateSha256);
        Assert.Equal(24, result.MinSdk);
        Assert.Equal(36, result.TargetSdk);
        Assert.Equal(new[] { "arm64-v8a", "armeabi-v7a", "x86_64" }, result.Architectures);
    }

    [Theory]
    [InlineData("package")]
    [InlineData("debuggable")]
    [InlineData("debug-certificate")]
    [InlineData("unknown-certificate")]
    [InlineData("multiple-signers")]
    [InlineData("invalid-version-code")]
    [InlineData("invalid-version-name")]
    [InlineData("unknown-abi")]
    public void Untrusted_QA_debug_or_malformed_release_metadata_is_rejected(string scenario)
    {
        var signature = Signature;
        var badging = Badging;
        switch (scenario)
        {
            case "package": badging = badging.Replace("com.pilotgo.app", "com.pilotgo.app.qa"); break;
            case "debuggable": badging += "application-debuggable\r\n"; break;
            case "debug-certificate": signature = signature.Replace("CN=PilotGo", "CN=Android Debug"); break;
            case "unknown-certificate": signature = signature.Replace(PortalArtifactOptions.OriginalCertificate, new string('a', 64)); break;
            case "multiple-signers": signature += "Signer #2 certificate SHA-256 digest: " + PortalArtifactOptions.OriginalCertificate + "\r\n"; break;
            case "invalid-version-code": badging = badging.Replace("versionCode='9'", "versionCode='0'"); break;
            case "invalid-version-name": badging = badging.Replace("versionName='2.0.1'", "versionName='../../file'"); break;
            case "unknown-abi": badging = badging.Replace("arm64-v8a", "unknown"); break;
        }
        var failure = Assert.Throws<PortalArtifactException>(() => OfficialAndroidApkInspector.ParseOutputs(signature, badging, new()));
        Assert.Equal(422, failure.StatusCode);
    }

    [Theory]
    [InlineData("Production", 0, 9)]
    [InlineData("Staging", 8, 9)]
    [InlineData("Testing", 8, 8)]
    [InlineData("Production", 14, 14)]
    public void Production_never_allows_a_minimum_below_the_distributed_version(string environment, int configured, int expected)
    {
        var host = new ArtifactTestEnvironment { EnvironmentName = environment };
        Assert.Equal(expected, new PortalArtifactOptions { MinimumVersionCode = configured }.EffectiveMinimumVersionCode(host));
    }

    [Fact]
    public void Archive_preflight_rejects_missing_manifest_traversal_and_excessive_expansion()
    {
        using var area = new ArtifactTestArea();
        var missing = Path.Combine(area.Root, "missing.apk");
        MakeArchive(missing, ("classes.dex", 10), ("other", 1));
        Assert.Throws<PortalArtifactException>(() => OfficialAndroidApkInspector.PreflightArchive(missing, 4096));
        var traversal = Path.Combine(area.Root, "traversal.apk");
        MakeArchive(traversal, ("AndroidManifest.xml", 10), ("classes.dex", 10), ("../outside", 1));
        Assert.Throws<PortalArtifactException>(() => OfficialAndroidApkInspector.PreflightArchive(traversal, 4096));
        var expanded = Path.Combine(area.Root, "expanded.apk");
        MakeArchive(expanded, ("AndroidManifest.xml", 100), ("classes.dex", 1000));
        Assert.Throws<PortalArtifactException>(() => OfficialAndroidApkInspector.PreflightArchive(expanded, 100));
        OfficialAndroidApkInspector.PreflightArchive(expanded, 4096);
    }

    [Theory]
    [InlineData("https://attacker.example")]
    [InlineData("http://envirapido.com")]
    [InlineData("https://envirapido.com/path")]
    [InlineData("https://user:secret@envirapido.com")]
    [InlineData("https://envirapido.com?token=x")]
    public void Upload_metadata_cannot_claim_another_origin_or_include_credentials(string origin)
    {
        Assert.Throws<PortalArtifactException>(() => PortalArtifactService.ValidateMetadata("Notes", new string('a', 40), origin, null));
    }

    private static void MakeArchive(string path, params (string Name, int Bytes)[] entries)
    {
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            using var stream = archive.CreateEntry(entry.Name).Open();
            stream.Write(new byte[entry.Bytes]);
        }
    }
}
