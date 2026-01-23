using Microsoft.AspNetCore.Identity;
using System.Linq;


namespace Login.Data
{
    public static class SeedData
    {
        public static async Task SeedRolesAndAdminAsync(IServiceProvider service)
        {
            var roleManager = service.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = service.GetRequiredService<UserManager<IdentityUser>>();

            // ✅ ROLES REALES DEL SISTEMA
            string[] roles = { "SuperAdmin", "Gestor", "Cliente", "Piloto", "Admin" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            // =========================
            // 1) SUPERADMIN (dueño del SaaS)
            // =========================
            await EnsureUserWithRole(userManager,
                email: "superadmin@local.com",
                password: "Admin123",
                role: "SuperAdmin");

            // =========================
            // 2) GESTOR (admin del tenant)
            // =========================
            await EnsureUserWithRole(userManager,
                email: "gestor@local.com",
                password: "Admin123",
                role: "Gestor");

            // =========================
            // 3) CLIENTE (usuario de tienda)
            // =========================
            await EnsureUserWithRole(userManager,
                email: "cliente@local.com",
                password: "Admin123",
                role: "Cliente");

            // =========================
            // 4) PILOTO
            // =========================
            await EnsureUserWithRole(userManager,
                email: "piloto@local.com",
                password: "Admin123",
                role: "Piloto");

            // =========================
            // 5) TU ADMIN ORIGINAL (si lo quieres mantener)
            // =========================
            await EnsureUserWithRole(userManager,
                email: "admin@local.com",
                password: "Admin123",
                role: "Admin");

            // ✅ OPCIONAL (modo dev): darle TODOS los roles al admin@local.com
            // (Así entras a cualquier vista sin crear mil usuarios)
            var admin = await userManager.FindByEmailAsync("admin@local.com");
            if (admin != null)
            {
                foreach (var r in new[] { "SuperAdmin", "Gestor", "Cliente", "Piloto" })
                {
                    if (!await userManager.IsInRoleAsync(admin, r))
                        await userManager.AddToRoleAsync(admin, r);
                }
            }
        }

        private static async Task EnsureUserWithRole(
            UserManager<IdentityUser> userManager,
            string email,
            string password,
            string role)
        {
            var user = await userManager.FindByEmailAsync(email);

            if (user == null)
            {
                user = new IdentityUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var created = await userManager.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    var msg = string.Join(" | ", created.Errors.Select(e => e.Description));
                    throw new Exception($"Error creando usuario {email}: {msg}");
                }
            }

            if (!await userManager.IsInRoleAsync(user, role))
                await userManager.AddToRoleAsync(user, role);
        }
    }
}
