// Formularios de la página Visores. Responden con una redirección (sin datos de pacientes en la URL).
using Microsoft.AspNetCore.Mvc;
using Rehab.Portal.Datos;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Acciones;

public static class AccionesVisores
{
    public static void MapAccionesVisores(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/visores").ExigirToken();

        g.MapPost("/aprobar", async ([FromForm] string codigo, [FromForm] string nombre, Visores repo) =>
            Volver(await repo.AprobarAsync(codigo, nombre), "aprobado"));

        g.MapPost("/{id}/asignar", async (ushort id, [FromForm] string? paciente, Visores repo) =>
            Volver(string.IsNullOrEmpty(paciente) ? "paciente" : await repo.AsignarAsync(id, paciente), "asignado"));

        g.MapPost("/{id}/quitar", async (ushort id, Visores repo) =>
        {
            await repo.QuitarAsync(id);
            return Volver(null, "quitado");
        });

        g.MapPost("/revocar", async ([FromForm] ushort visor, [FromForm] string? confirmar, Visores repo) =>
        {
            if (confirmar != "si") return Volver("confirmar", "");
            await repo.RevocarAsync(visor);
            return Volver(null, "revocado");
        });
    }

    private static IResult Volver(string? error, string ok) =>
        Results.Redirect(error is null ? $"/visores?ok={ok}" : $"/visores?error={error}");
}
