// Cookie de 30 días renovable. Si el sello cambia o la cuenta se desactiva, la sesión se cierra.
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Rehab.Portal.Datos;

namespace Rehab.Portal.Seguridad;

public static class Sesion
{
    public const string Id = "tid";
    public const string Sello = "sello";
    public const string Temporal = "temporal";

    public static Task EntrarAsync(HttpContext http, Terapeuta t, string? sello = null, bool? temporal = null)
    {
        var identidad = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, t.Usuario),
            new Claim(Id, t.Id.ToString()),
            new Claim(Sello, sello ?? t.Sello),
            new Claim(Temporal, (temporal ?? t.DebeCambiar) ? "1" : "0"),
        ], CookieAuthenticationDefaults.AuthenticationScheme);

        return http.SignInAsync(new ClaimsPrincipal(identidad), new AuthenticationProperties { IsPersistent = true });
    }

    public static ushort IdDe(ClaimsPrincipal u) => ushort.Parse(u.FindFirstValue(Id)!);

    public static async Task ValidarAsync(CookieValidatePrincipalContext ctx)
    {
        var u = ctx.Principal;
        var repo = ctx.HttpContext.RequestServices.GetRequiredService<Terapeutas>();
        bool vigente = u is not null
            && ushort.TryParse(u.FindFirstValue(Id), out ushort id)
            && u.FindFirstValue(Sello) is { } sello
            && await repo.SesionVigenteAsync(id, sello);

        if (!vigente)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
