using Login.Application.Operations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Login.ViewModels.Envirapido;

public sealed class ReasignacionesPanelVm
{
    public string Empresa { get; init; } = "";
    public int? OrdenId { get; init; }
    public string? Pedido { get; init; }
    public DateTime ActualizadoAt { get; init; }
    public ReasignacionSnapshot Datos { get; init; } = new([], []);
    public List<SelectListItem> Pilotos { get; init; } = [];
    public List<SelectListItem> Comercios { get; init; } = [];
    public Dictionary<int, string> PilotosNombres { get; init; } = [];
    public string Piloto(int? id) => id.HasValue ? PilotosNombres.GetValueOrDefault(id.Value, "Piloto no disponible") : "Sin asignar";
}
