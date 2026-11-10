using System.Text;
using Microsoft.AspNetCore.Mvc;
using Rehab.Portal.Datos;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Acciones;

public static class AccionesPacientes
{
    public static void MapAccionesPacientes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pacientes/{codigo}/exportar", async (string codigo, Pacientes repo) =>
        {
            if (await repo.BuscarAsync(codigo) is null) return Results.NotFound();
            byte[] contenido = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(await repo.ExportarCsvAsync(codigo))).ToArray();
            return Results.File(contenido, "text/csv; charset=utf-8", $"{codigo}_datos.csv");
        });

        app.MapPost("/pacientes/{codigo}/eliminar", async (string codigo, [FromForm] string? confirmar, Pacientes repo) =>
        {
            if (!string.Equals(confirmar?.Trim(), codigo, StringComparison.OrdinalIgnoreCase))
                return Results.Redirect($"/pacientes/{codigo}?error=confirmar");
            return Results.Redirect(await repo.EliminarAsync(codigo) ? "/pacientes?eliminado=1" : $"/pacientes/{codigo}?error=eliminar");
        }).ExigirToken();
    }
}
