using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Envirapido.Portal.Api.Data;
using Envirapido.Portal.Api.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Envirapido.Portal.Api.Services;

public interface IPortalMailTransport { Task SendAsync(string to, string subject, string html, CancellationToken ct = default); }
public sealed class PortalSmtpTransport(PortalOptions options) : IPortalMailTransport
{
    public async Task SendAsync(string to, string subject, string html, CancellationToken ct = default)
    {
        if (!options.Mail.Enabled) throw new InvalidOperationException("SMTP del portal no está configurado.");
        using var smtp = new SmtpClient(options.Mail.Host, options.Mail.Port) { EnableSsl = options.Mail.UseSsl, UseDefaultCredentials = false };
        if (!string.IsNullOrEmpty(options.Mail.UserName)) smtp.Credentials = new NetworkCredential(options.Mail.UserName, options.Mail.Password);
        using var message = new MailMessage { From = new MailAddress(options.Mail.FromAddress, options.Mail.FromName), Subject = subject, Body = html, IsBodyHtml = true };
        message.To.Add(new MailAddress(to)); await smtp.SendMailAsync(message, ct);
    }
}
public sealed record PortalMailMessage(string To, string Subject, string Html);
public sealed class PortalMailService(PortalDbContext db, IDataProtectionProvider protection, PortalOptions options, IHostEnvironment env, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("Envirapido.Portal.Outbox.v1");
    public void EnsureAvailable()
    {
        if (!options.Mail.Enabled && !PortalOptions.Local(env)) throw new PortalException(503, "EMAIL_UNAVAILABLE", "El correo del portal no está disponible. Contacta a administración.");
    }
    public void Enqueue(string to, string subject, string html)
    {
        EnsureAvailable();
        db.Outbox.Add(new PortalOutbox { ProtectedMessage = protector.Protect(JsonSerializer.Serialize(new PortalMailMessage(to, subject, html))), CreatedAt = clock.GetUtcNow(), NextAttemptAt = clock.GetUtcNow() });
    }
    public PortalMailMessage Read(PortalOutbox row) => JsonSerializer.Deserialize<PortalMailMessage>(protector.Unprotect(row.ProtectedMessage))!;
}
public sealed class PortalOutboxWorker(IServiceScopeFactory scopes, IHostEnvironment env, TimeProvider clock, ILogger<PortalOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (env.IsEnvironment("Testing")) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try { while (await timer.WaitForNextTickAsync(stoppingToken)) await DispatchAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    public async Task DispatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
        try
        {
            var now = clock.GetUtcNow();
            var ids = await db.Outbox.AsNoTracking().Where(o => o.SentAt == null && o.NextAttemptAt <= now && (o.ProcessingUntil == null || o.ProcessingUntil < now)).OrderBy(o => o.CreatedAt).Take(10).Select(o => o.Id).ToListAsync(ct);
            foreach (var id in ids)
            {
                var acquired = await db.Outbox.Where(o => o.Id == id && o.SentAt == null && (o.ProcessingUntil == null || o.ProcessingUntil < now))
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.ProcessingUntil, now.AddMinutes(2)).SetProperty(o => o.Attempts, o => o.Attempts + 1), ct);
                if (acquired != 1) continue;
                var row = await db.Outbox.SingleAsync(o => o.Id == id, ct);
                try
                {
                    var mail = scope.ServiceProvider.GetRequiredService<PortalMailService>().Read(row);
                    await scope.ServiceProvider.GetRequiredService<IPortalMailTransport>().SendAsync(mail.To, mail.Subject, mail.Html, ct);
                    row.SentAt = clock.GetUtcNow(); row.ProtectedMessage = ""; row.LastError = null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { row.LastError = "No se pudo enviar el mensaje; se reintentará."; row.NextAttemptAt = clock.GetUtcNow().AddSeconds(Math.Min(3600, 30 * Math.Pow(2, Math.Min(7, row.Attempts)))); }
                row.ProcessingUntil = null; await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogWarning("El outbox del portal no estuvo disponible; se reintentará sin revelar mensajes ni credenciales."); }
    }
}
