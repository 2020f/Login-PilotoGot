using System.Collections.Generic;

namespace Login.ViewModels.SuperAdmin
{
    public class ClientesIndexVm
    {
        public string? Ok { get; set; }
        public string? Error { get; set; }

        public List<ClienteAppRowVm> Clientes { get; set; } = new();
    }
}
