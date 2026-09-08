namespace Login.Domain.Entities
{
    public class OrdenAutoRechazo
    {
        public int Id { get; set; }

        public int OrdenEntregaId { get; set; }
        public OrdenEntrega OrdenEntrega { get; set; } = null!;

        public int PilotoId { get; set; }
        public Piloto Piloto { get; set; } = null!;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
