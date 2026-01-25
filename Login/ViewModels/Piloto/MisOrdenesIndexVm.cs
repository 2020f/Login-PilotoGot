using System.Collections.Generic;

namespace Login.ViewModels.Piloto
{
    public class MisOrdenesIndexVm
    {
        public string? Ok { get; set; }
        public string? Error { get; set; }

        // Tú dijiste “solo visualiza una orden”, por eso lo dejamos como lista pero usaremos 1.
        public List<MisOrdenesRowVm> Ordenes { get; set; } = new();
    }
}
