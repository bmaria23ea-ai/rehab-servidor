using Microsoft.AspNetCore.Mvc;
using Rehab.Portal.Datos;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Acciones;

public static class AccionesSesiones
{
    public static void MapAccionesSesiones(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sesiones/{id}/asignar", async (ulong id, [FromForm] string? paciente, Sesiones repo) =>
            Results.Redirect(!string.IsNullOrEmpty(paciente) && await repo.AsignarAsync(id, paciente)
                ? $"/sesiones/{id}?ok=asignada"
                : $"/sesiones/{id}?error=asignar"))
           .ExigirToken();
    }
}
