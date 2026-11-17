using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Envirapido.Portal.Api.Artifacts;

public sealed record ApkInspectionResult(string PackageName, string VersionName, int VersionCode,
    string CertificateSha256, IReadOnlyList<string> Architectures, int? MinSdk, int? TargetSdk);

public interface IApkInspector
{
    bool IsConfigured { get; }
    Task<ApkInspectionResult> InspectAsync(string path, CancellationToken cancellationToken);
}

public sealed class OfficialAndroidApkInspector(IOptionsMonitor<PortalArtifactOptions> options) : IApkInspector
{
    public bool IsConfigured => IsTool(options.CurrentValue.JavaPath, "java", "java.exe")
        && IsTool(options.CurrentValue.Aapt2Path, "aapt2", "aapt2.exe")
        && Path.IsPathFullyQualified(options.CurrentValue.ApkSignerJarPath)
        && Path.GetFileName(options.CurrentValue.ApkSignerJarPath).Equals("apksigner.jar", StringComparison.OrdinalIgnoreCase)
        && File.Exists(options.CurrentValue.ApkSignerJarPath);

    private static bool IsTool(string path, params string[] names) => Path.IsPathFullyQualified(path)
        && File.Exists(path) && names.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    public async Task<ApkInspectionResult> InspectAsync(string path, CancellationToken cancellationToken)
    {
        if (!IsConfigured) throw new PortalArtifactException(503, "Las herramientas oficiales de validación Android no están configuradas.");
        PreflightArchive(path, options.CurrentValue.ExpandedLimit);
        var settings = options.CurrentValue;
        var signed = await RunAsync(settings.JavaPath,
            ["-Xmx256m", "-Djava.awt.headless=true", "-jar", settings.ApkSignerJarPath, "verify", "--verbose", "--print-certs", path],
            settings.ToolTimeoutSeconds, cancellationToken);
        if (signed.ExitCode != 0) throw new PortalArtifactException(422, "La firma del APK no es válida. Sube una versión release firmada.");
        var badging = await RunAsync(settings.Aapt2Path, ["dump", "badging", path], settings.ToolTimeoutSeconds, cancellationToken);
        if (badging.ExitCode != 0) throw new PortalArtifactException(422, "No se pudo leer el manifiesto Android del APK.");
        return ParseOutputs(signed.Output, badging.Output, settings);
    }

    public static void PreflightArchive(string path, long expandedLimit)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> magic = stackalloc byte[4];
            if (file.Read(magic) != 4 || !magic.SequenceEqual(new byte[] { 0x50, 0x4b, 0x03, 0x04 }))
                throw new PortalArtifactException(422, "El archivo no tiene formato APK.");
            file.Position = 0;
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            if (archive.Entries.Count is < 2 or > 20000 || archive.GetEntry("AndroidManifest.xml") is not { Length: > 0 and <= 16 * 1024 * 1024 }
                || archive.GetEntry("classes.dex") is not { Length: > 0 })
                throw new PortalArtifactException(422, "El archivo no contiene una aplicación Android válida.");
            long expanded = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.StartsWith('/') || entry.FullName.Contains('\\')
                    || entry.FullName.Split('/').Any(segment => segment == ".."))
                    throw new PortalArtifactException(422, "El APK contiene rutas no válidas.");
                if (entry.Length > expandedLimit - expanded)
                    throw new PortalArtifactException(422, "El contenido expandido del APK supera el límite permitido.");
                expanded += entry.Length;
            }
        }
        catch (InvalidDataException) { throw new PortalArtifactException(422, "El archivo APK está incompleto o dañado."); }
    }

    public static ApkInspectionResult ParseOutputs(string signature, string badging, PortalArtifactOptions settings)
    {
        var digests = Regex.Matches(signature, @"^Signer #\d+ certificate SHA-256 digest:\s*([a-fA-F0-9]{64})\s*$", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value.ToLowerInvariant()).ToArray();
        if (digests.Length != 1 || !settings.IsCertificateAllowed(digests[0]))
            throw new PortalArtifactException(422, "El certificado del APK no corresponde a la firma definitiva autorizada.");
        if (Regex.IsMatch(signature, @"certificate DN:.*CN=Android Debug", RegexOptions.IgnoreCase)
            || Regex.IsMatch(badging, @"^application-debuggable\s*$", RegexOptions.Multiline))
            throw new PortalArtifactException(422, "No se admiten APK de depuración o QA.");
        var packageLine = Regex.Match(badging, @"^package:\s+([^\r\n]+)", RegexOptions.Multiline).Groups[1].Value;
        string Attribute(string name) => Regex.Match(packageLine, @"(?:^|\s)" + Regex.Escape(name) + @"='([^']*)'").Groups[1].Value;
        var packageName = Attribute("name");
        var versionName = Attribute("versionName");
        if (!string.Equals(packageName, PortalArtifactOptions.PackageName, StringComparison.Ordinal))
            throw new PortalArtifactException(422, "El paquete no corresponde a la aplicación Envirapido de producción.");
        if (!int.TryParse(Attribute("versionCode"), out var code) || code <= 0
            || !Regex.IsMatch(versionName, @"^[0-9A-Za-z][0-9A-Za-z._+\-]{0,79}$"))
            throw new PortalArtifactException(422, "El APK no declara una versión Android válida.");
        var nativeLine = Regex.Match(badging, @"^native-code:\s*([^\r\n]+)", RegexOptions.Multiline).Groups[1].Value;
        var architectures = Regex.Matches(nativeLine, "'([^']+)'").Select(match => match.Groups[1].Value).ToArray();
        var permitted = new[] { "arm64-v8a", "armeabi-v7a", "x86_64", "x86" };
        if (architectures.Length == 0 || architectures.Any(abi => !permitted.Contains(abi, StringComparer.Ordinal)))
            throw new PortalArtifactException(422, "El APK no declara arquitecturas Android compatibles.");
        int? Sdk(string key) => int.TryParse(Regex.Match(badging, @"^" + key + @":'([0-9]+)'", RegexOptions.Multiline).Groups[1].Value, out var number) ? number : null;
        return new ApkInspectionResult(packageName, versionName, code, digests[0], architectures,
            Sdk("minSdkVersion") ?? Sdk("sdkVersion"), Sdk("targetSdkVersion"));
    }

    private sealed record ToolResult(int ExitCode, string Output);

    private static async Task<ToolResult> RunAsync(string executable, string[] arguments, int seconds, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable)!
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var inheritedOption in new[] { "JAVA_TOOL_OPTIONS", "_JAVA_OPTIONS", "JDK_JAVA_OPTIONS" }) start.Environment.Remove(inheritedOption);
        using var process = new Process { StartInfo = start };
        void StopProcess()
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 60)));
        try
        {
            if (!process.Start()) throw new PortalArtifactException(503, "No se pudo iniciar la validación Android.");
            var output = ReadBoundedAsync(process.StandardOutput, deadline.Token, StopProcess);
            var error = ReadBoundedAsync(process.StandardError, deadline.Token, StopProcess);
            var readers = Task.WhenAll(output, error);
            // A reader exceeding the output limit must also stop the process,
            // rather than deadlocking on its redirected output pipe.
            var exited = process.WaitForExitAsync(deadline.Token);
            var first = await Task.WhenAny(readers, exited);
            if (first == readers && readers.IsFaulted) await readers;
            await exited;
            var text = await readers;
            return new ToolResult(process.ExitCode, text[0] + "\n" + text[1]);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new PortalArtifactException(503, "La validación Android excedió su tiempo máximo. Puedes reintentarla."); }
        catch (System.ComponentModel.Win32Exception)
        { throw new PortalArtifactException(503, "No están disponibles las herramientas oficiales de validación Android."); }
        finally { StopProcess(); }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken, Action stopProcess)
    {
        var text = new StringBuilder();
        var buffer = new char[4096];
        int length;
        while ((length = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            if (text.Length + length > 1024 * 1024)
            {
                stopProcess();
                throw new PortalArtifactException(422, "El manifiesto o la firma del APK exceden los límites de validación.");
            }
            text.Append(buffer, 0, length);
        }
        return text.ToString();
    }
}
