using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Logging;

public class EmailSender : IEmailSender
{
    private readonly ILogger<EmailSender> _logger;

    public EmailSender(ILogger<EmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        // 📧 Simulación (sale en consola / logs)
        _logger.LogInformation("EMAIL A: {email}", email);
        _logger.LogInformation("ASUNTO: {subject}", subject);
        _logger.LogInformation("MENSAJE: {message}", htmlMessage);

        return Task.CompletedTask;
    }
}
