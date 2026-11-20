using System.Text.Json;
using System.Text.Json.Serialization;

namespace Login.Serialization;

/// <summary>
/// Los instantes del dominio se guardan en UTC. SQL Server datetime2 pierde
/// DateTime.Kind al materializarlos; el contrato HTTP debe seguir indicando Z.
/// Solo afecta JSON MVC, no formularios, filtros por fecha ni datos internos.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.GetDateTime(); // Conservar la interpretación entrante predeterminada de System.Text.Json.

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        var utc = value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();
        writer.WriteStringValue(utc);
    }
}
