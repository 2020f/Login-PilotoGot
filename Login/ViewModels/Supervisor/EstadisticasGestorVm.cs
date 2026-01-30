using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Login.ViewModels.Supervisor
{
    public class EstadisticasGestorVm
    {
        // Filtros
        public DateTime Desde { get; set; }
        public DateTime Hasta { get; set; }
        public int? TiendaId { get; set; }
        public int? PilotoId { get; set; }

        // Dropdowns
        public List<SelectListItem> Tiendas { get; set; } = new();
        public List<SelectListItem> Pilotos { get; set; } = new();

        // Data
        public EstadisticasCardsVm Cards { get; set; } = new();
        public List<OrdenesPorDiaVm> PorDia { get; set; } = new();

        public List<RankingPilotoVm> TopPilotos { get; set; } = new();
        public List<RankingTiendaVm> TopTiendas { get; set; } = new();

        public TiemposProcesoVm Tiempos { get; set; } = new();
    }

    public class EstadisticasCardsVm
    {
        public int Total { get; set; }
        public int Entregadas { get; set; }
        public int Pendientes { get; set; }
        public int Incidentes { get; set; }
        public int PorcentajeEntrega { get; set; }
    }

    public class OrdenesPorDiaVm
    {
        public DateTime Dia { get; set; }
        public int Total { get; set; }
        public int Entregadas { get; set; }
        public int Incidentes { get; set; }
    }

    public class RankingPilotoVm
    {
        public int PilotoId { get; set; }
        public string Nombre { get; set; } = "";
        public int Total { get; set; }
        public int Entregadas { get; set; }
        public int Incidentes { get; set; }
        public int PromedioMinutosEntrega { get; set; }
    }

    public class RankingTiendaVm
    {
        public int TiendaId { get; set; }
        public string Nombre { get; set; } = "";
        public int Total { get; set; }
        public int Entregadas { get; set; }
        public int Incidentes { get; set; }
    }

    public class TiemposProcesoVm
    {
        public double MinCreadaAsignada { get; set; }
        public double MinAsignadaRecolectada { get; set; }
        public double MinRecolectadaEntregada { get; set; }
        public double MinTotalCreadaEntregada { get; set; }

        public void RoundAll()
        {
            MinCreadaAsignada = Math.Round(MinCreadaAsignada, 0);
            MinAsignadaRecolectada = Math.Round(MinAsignadaRecolectada, 0);
            MinRecolectadaEntregada = Math.Round(MinRecolectadaEntregada, 0);
            MinTotalCreadaEntregada = Math.Round(MinTotalCreadaEntregada, 0);
        }
    }
}
