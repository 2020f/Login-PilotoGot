namespace Login.Application.Exceptions;

public sealed class OperacionOrdenConflictoException(string message) : InvalidOperationException(message);

/// <summary>Validación conocida antes de escribir y bajo el bloqueo de creación.</summary>
public sealed class OrdenSolicitudRechazadaException(string message) : InvalidOperationException(message);
