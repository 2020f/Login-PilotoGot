using System.Net;
using System.Net.Mail;
using Login.Services;
using Microsoft.Extensions.Options;

/// <summary>Entrega configurada; nunca escribe destinatarios, enlaces o tokens en logs.</summary>
public sealed class EmailSender(IOptions<AccountEmailOptions> options, ILogger<EmailSender> logger) : IAccountEmailSender
{
    private readonly AccountEmailOptions _options = options.Value;
    public Uri? PublicBaseUri => AccountEmailOptions.ReadPublicBaseUri(_options.PublicBaseUrl);
    public bool IsConfigured => _options.Enabled && PublicBaseUri is not null
        && !string.IsNullOrWhiteSpace(_options.Host) && _options.Port is > 0 and <= 65535
        && MailAddress.TryCreate(_options.FromAddress, out _)
        && (string.IsNullOrWhiteSpace(_options.UserName) == string.IsNullOrWhiteSpace(_options.Password));

    public async Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        if (!IsConfigured) throw new AccountEmailDeliveryException();
        using var message = new MailMessage
        {
            From = new MailAddress(_options.FromAddress, _options.FromName),
            Subject = subject, Body = htmlMessage, IsBodyHtml = true
        };
        message.To.Add(new MailAddress(email));
        using var smtp = new SmtpClient(_options.Host.Trim(), _options.Port)
        {
            EnableSsl = true, UseDefaultCredentials = false, DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = string.IsNullOrWhiteSpace(_options.UserName) ? null : new NetworkCredential(_options.UserName, _options.Password)
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await smtp.SendMailAsync(message, timeout.Token); }
        catch (SmtpException ex)
        {
            logger.LogWarning("No se pudo entregar un correo de cuenta. Estado SMTP: {SmtpStatus}", (int)ex.StatusCode);
            throw new AccountEmailDeliveryException();
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Se agotó el tiempo de entrega de un correo de cuenta.");
            throw new AccountEmailDeliveryException();
        }
    }
}