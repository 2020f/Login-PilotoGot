using Microsoft.AspNetCore.Identity.UI.Services;

namespace Login.Services;

public interface IAccountEmailSender : IEmailSender
{
    bool IsConfigured { get; }
    Uri? PublicBaseUri { get; }
}

public sealed class AccountEmailOptions
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "Envirapido";
    public string PublicBaseUrl { get; set; } = "";

    // Los enlaces de seguridad no dependen de Host/Forwarded-Host del solicitante.
    // HTTP se limita a loopback para pruebas locales explícitas.
    public static Uri? ReadPublicBaseUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
            ? uri : null;
}

public sealed class AccountEmailDeliveryException : Exception
{
    public AccountEmailDeliveryException() : base("La entrega de correo de cuenta no está disponible.") { }
}
