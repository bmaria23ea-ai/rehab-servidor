// Formularios de la página Visores. Responden con una redirección (sin datos de pacientes en la URL).
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Rehab.Portal.Datos;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Acciones;

public static class AccionesVisores
{
    public static void MapAccionesVisores(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/visores").ExigirToken();

        g.MapPost("/aprobar", async ([FromForm] string codigo, [FromForm] string nombre, Visores repo, ClaimsPrincipal u, Auditoria log) =>
        {
            string? error = await repo.AprobarAsync(codigo, nombre);
            if (error is null) await log.RegistrarAsync(u, "aprobar_gafas", nombre.Trim());
            return Volver(error, "aprobado");
        });

        g.MapPost("/{id}/asignar", async (ushort id, [FromForm] string? paciente, Visores repo, ClaimsPrincipal u, Auditoria log) =>
        {
            string? error = string.IsNullOrEmpty(paciente) ? "paciente" : await repo.AsignarAsync(id, paciente);
            if (error is null) await log.RegistrarAsync(u, "asignar_gafas", $"{id} {paciente}");
            return Volver(error, "asignado");
        });

        g.MapPost("/{id}/quitar", async (ushort id, Visores repo, ClaimsPrincipal u, Auditoria log) =>
        {
            await repo.QuitarAsync(id);
            await log.RegistrarAsync(u, "quitar_gafas", id.ToString());
            return Volver(null, "quitado");
        });

        g.MapPost("/revocar", async ([FromForm] ushort visor, [FromForm] string? confirmar, Visores repo, ClaimsPrincipal u, Auditoria log) =>
        {
            if (confirmar != "si") return Volver("confirmar", "");
            await repo.RevocarAsync(visor);
            await log.RegistrarAsync(u, "desactivar_gafas", visor.ToString());
            return Volver(null, "revocado");
        });
    }

    private static IResult Volver(string? error, string ok) =>
        Results.Redirect(error is null ? $"/visores?ok={ok}" : $"/visores?error={error}");
}
