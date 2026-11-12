using System.Security.Claims;
using Login.ViewModels.SuperAdmin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Login.Controllers;

public partial class SuperAdminController
{
    [HttpGet]
    public async Task<IActionResult> Accesos(string? q = null, int pagina = 1, string? ok = null, string? error = null)
    {
        pagina = Math.Clamp(pagina, 1, 1000000);
        var query = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) { q = q.Trim()[..Math.Min(100,q.Trim().Length)]; query = query.Where(u => u.Email != null && u.Email.Contains(q)); }
        var users = await query.OrderBy(u=>u.Email).Skip((pagina-1)*20).Take(20).ToListAsync();
        var rows = new List<AccesoUsuarioVm>();
        foreach(var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var stores = _db.UsuariosTienda.Where(u=>u.IdentityUserId==user.Id).Select(u=>u.Tienda.ClienteAppId);
            var pilots = _db.Pilotos.Where(p=>p.IdentityUserId==user.Id).Select(p=>p.ClienteAppId);
            var managers = _db.ClientesApp.Where(t=>t.GestorIdentityUserId==user.Id).Select(t=>t.Id);
            var ids = await stores.Union(pilots).Union(managers).ToListAsync();
            var names = await _db.ClientesApp.Where(t=>ids.Contains(t.Id)).Select(t=>t.NombreComercial).ToListAsync();
            rows.Add(new AccesoUsuarioVm { Id=user.Id,Email=user.Email,Roles=string.Join(", ",roles),Empresas=string.Join(", ",names),
                SesionesMoviles=await _db.UserTokens.CountAsync(t=>t.UserId==user.Id && t.LoginProvider=="PilotGoMobile"),
                Bloqueado=user.LockoutEnd>_clock.GetUtcNow(),DobleFactor=user.TwoFactorEnabled,Protegido=roles.Contains("SuperAdmin") });
        }
        return View(new AccesosVm {Usuarios=rows,Total=await query.CountAsync(),Pagina=pagina,Q=q,Ok=ok,Error=error});
    }

    [HttpPost,ValidateAntiForgeryToken]
    public async Task<IActionResult> RevocarSesiones(string userId,string motivo,bool confirmado)
    {
        if (!confirmado || string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length>500)
            return RedirectToAction(nameof(Accesos),new{error="Confirma la revocación e indica un motivo de hasta 500 caracteres."});
        var user=await _userManager.FindByIdAsync(userId);
        if(user==null) return NotFound();
        if(await _userManager.IsInRoleAsync(user,"SuperAdmin"))
            return RedirectToAction(nameof(Accesos),new{error="Las cuentas de plataforma están protegidas. Gestiona sus sesiones mediante el procedimiento de seguridad del propietario."});
        // Operación global intencional y explícita sobre una identidad; no cambia
        // sus roles, pertenencias, contraseñas ni las asignaciones de órdenes.
        await using var tx=await _db.Database.BeginTransactionAsync();
        var result=await _userManager.UpdateSecurityStampAsync(user);
        if(!result.Succeeded) return RedirectToAction(nameof(Accesos),new{error="No se pudieron revocar las sesiones."});
        await _db.UserTokens.Where(t=>t.UserId==user.Id && t.LoginProvider=="PilotGoMobile").ExecuteDeleteAsync();
        Audit(null,"SesionesRevocadas","Usuario",user.Id,$"Acceso de {user.Email}; {motivo.Trim()}");
        await _db.SaveChangesAsync(); await tx.CommitAsync();
        return RedirectToAction(nameof(Accesos),new{ok="Sesiones móviles revocadas. La cuenta conserva sus permisos y puede volver a iniciar sesión. Las sesiones web caducan en su siguiente validación de seguridad."});
    }
}
