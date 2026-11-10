using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Rehab.Portal.Datos;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Acciones;

public static class AccionesSesiones
{
    public static void MapAccionesSesiones(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sesiones/{id}/asignar", async (ulong id, [FromForm] string? paciente, Sesiones repo, ClaimsPrincipal u, Auditoria log) =>
        {
            if (string.IsNullOrEmpty(paciente) || !await repo.AsignarAsync(id, paciente))
                return Results.Redirect($"/sesiones/{id}?error=asignar");
            await log.RegistrarAsync(u, "asignar_sesion", $"{id} {paciente}");
            return Results.Redirect($"/sesiones/{id}?ok=asignada");
        }).ExigirToken();
    }
}
