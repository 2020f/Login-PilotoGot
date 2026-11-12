namespace Login.Application.Operations;

public static class OperationsGpsPolicy
{
    // Freshness describes a position. Authorization is a separate, short lived lease.
    public static int FreshnessSeconds(IConfiguration configuration)
        => Math.Clamp(configuration.GetValue<int?>("Envirapido:GpsFrescuraSegundos") ?? 120, 30, 900);
}
