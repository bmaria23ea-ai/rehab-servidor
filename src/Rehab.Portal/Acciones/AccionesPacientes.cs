using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Rehab.Portal.Datos;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Acciones;

public static class AccionesPacientes
{
    public static void MapAccionesPacientes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pacientes/{codigo}/exportar", async (string codigo, Pacientes repo, Exportacion excel, ClaimsPrincipal u, Auditoria log) =>
        {
            var p = await repo.BuscarAsync(codigo.ToUpperInvariant());
            if (p is null) return Results.NotFound();
            await log.RegistrarAsync(u, "exportar_paciente", p.Codigo);
            return Results.File(await excel.CrearAsync(p.Codigo), Excel.Tipo, $"{p.Codigo}_{Hora.HoyEcuador():yyyy-MM-dd}.xlsx");
        });

        app.MapPost("/pacientes/{codigo}/eliminar", async (string codigo, [FromForm] string? confirmar, Pacientes repo, ClaimsPrincipal u, Auditoria log) =>
        {
            if (!string.Equals(confirmar?.Trim(), codigo, StringComparison.OrdinalIgnoreCase))
                return Results.Redirect($"/pacientes/{codigo}?error=confirmar");
            if (!await repo.EliminarAsync(codigo)) return Results.Redirect($"/pacientes/{codigo}?error=eliminar");
            await log.RegistrarAsync(u, "eliminar_paciente", codigo.ToUpperInvariant());
            return Results.Redirect("/pacientes?eliminado=1");
        }).ExigirToken();
    }
}
