namespace Login.Domain.Enums
{
    public enum EstadoClienteApp
    {
        Activo = 1,
        Suspendido = 2,
        Vencido = 3
    }

    public enum EstadoOrden
    {
        Creada = 1,
        Asignada = 2,
        Recolectada = 3,
        Entregada = 4,
        Incidente = 5
    }

    public enum TipoCodigo
    {
        B_Recoleccion = 1,
        C_Finalizacion = 2
    }

    public enum EstadoPiloto
    {
        Disponible = 1,
        Ocupado = 2,
        Inactivo = 3
    }

    public enum TipoEventoOrden
    {
        Creada = 1,
        Asignada = 2,
        Recolectada = 3,
        Entregada = 4,
        Incidente = 5,
        Comentario = 6
    }
}
