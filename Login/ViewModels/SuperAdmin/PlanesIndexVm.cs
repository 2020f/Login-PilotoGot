using System.Collections.Generic;
using Login.Domain.Entities;

namespace Login.ViewModels.SuperAdmin
{
    public class PlanesIndexVm
    {
        public string? Ok { get; set; }
        public string? Error { get; set; }

        public List<Plan> Planes { get; set; } = new();
    }
}
