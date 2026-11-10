using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Rehab.Portal.Datos;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Acciones;

public static class AccionesPacientes
{
    public static void MapAccionesPacientes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pacientes/{codigo}/exportar", async (string codigo, Pacientes repo, ClaimsPrincipal u, Auditoria log) =>
        {
            if (await repo.BuscarAsync(codigo) is null) return Results.NotFound();
            await log.RegistrarAsync(u, "exportar_paciente", codigo.ToUpperInvariant());
            byte[] contenido = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(await repo.ExportarCsvAsync(codigo))).ToArray();
            return Results.File(contenido, "text/csv; charset=utf-8", $"{codigo}_datos.csv");
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
