using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
namespace Envirapido.Portal.Api.Data;

// Orden y comparaciones UTC idénticos en SQL Server y en SQLite del arnés.
public sealed class PortalUtcTicksConverter() : ValueConverter<DateTimeOffset, long>(value => value.UtcDateTime.Ticks, value => new DateTimeOffset(value, TimeSpan.Zero));
