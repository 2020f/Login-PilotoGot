using System.Collections.Generic;

namespace Login.ViewModels.Piloto
{
    public class MisOrdenesIndexVm
    {
        public string? Ok { get; set; }
        public string? Error { get; set; }

        public List<MisOrdenesRowVm> Ordenes { get; set; } = new();
    }
}
