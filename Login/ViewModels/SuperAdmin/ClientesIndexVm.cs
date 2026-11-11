using System.Collections.Generic;

namespace Login.ViewModels.SuperAdmin
{
    public class ClientesIndexVm
    {
        public string? Q { get; set; }
        public int Pagina { get; set; } = 1;
        public int Total { get; set; }
        public string? Ok { get; set; }
        public string? Error { get; set; }

        public List<ClienteAppRowVm> Clientes { get; set; } = new();
    }
}
