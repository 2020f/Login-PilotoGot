namespace Login.ViewModels.Tienda
{
    public class QrCodigosVm
    {
        public int OrdenId { get; set; }
        public int TiendaId { get; set; }
        public long NumeroOrdenA { get; set; }
        public string? CodigoA { get; set; }
        public string CodigoVisibleA => CodigoA ?? NumeroOrdenA.ToString();
        public string Estado { get; set; } = "";

        public string CodigoB { get; set; } = "";
        public string CodigoC { get; set; } = "";

        public string UsuarioFinalNombre { get; set; } = "";
        public string DireccionUbicacion { get; set; } = "";
        public string? MapaLink { get; set; }
        public string? Descripcion { get; set; }
    }
}
