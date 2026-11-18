namespace Login.Application.Operations;

public sealed record RegistrarAjusteRetiroRequest(decimal Importe, string Motivo, string Version, string OperacionId);
