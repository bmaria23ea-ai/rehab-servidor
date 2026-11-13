using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Rehab.Portal.Datos;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Acciones;

public static class AccionesSesiones
{
    public static void MapAccionesSesiones(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sesiones/exportar", async (Exportacion excel, ClaimsPrincipal u, Auditoria log) =>
        {
            await log.RegistrarAsync(u, "exportar_todo");
            return Results.File(await excel.CrearAsync(null), Excel.Tipo, $"pacientes_{Hora.HoyEcuador():yyyy-MM-dd}.xlsx");
        });

        app.MapGet("/sesiones/{id}/exportar", async (ulong id, Sesiones repo, Exportacion excel, ClaimsPrincipal u, Auditoria log) =>
        {
            if ((await repo.BuscarAsync(id))?.Sesion is not { Ejercicio: "caminata_6m" } s) return Results.NotFound();
            await log.RegistrarAsync(u, "exportar_sesion", $"Sesión {s.Codigo}");
            string nombre = $"{(s.Paciente is null ? "Sin_paciente" : s.Paciente)}_Sesion_{s.Codigo}.xlsx";
            return Results.File(await excel.CrearAsync(null, id), Excel.Tipo, nombre);
        });

        app.MapPost("/sesiones/{id}/asignar", async (ulong id, [FromForm] string? paciente, [FromForm] string? volver,
            Sesiones repo, ClaimsPrincipal u, Auditoria log) =>
        {
            string destino = volver == "inicio" ? "/" : $"/sesiones/{id}";
            if (string.IsNullOrEmpty(paciente) || !await repo.AsignarAsync(id, paciente))
                return Results.Redirect($"{destino}?error=asignar");
            await log.RegistrarAsync(u, "asignar_sesion", $"{await repo.NombreAsync(id)} a {paciente}");
            return Results.Redirect($"{destino}?ok=asignada");
        }).ExigirToken();

        app.MapPost("/sesiones/{id}/paciente", async (ulong id, [FromForm] string? paciente,
            Sesiones repo, ClaimsPrincipal u, Auditoria log) =>
        {
            var d = await repo.BuscarAsync(id);
            if (d is null) return Results.NotFound();
            string? nuevo = string.IsNullOrEmpty(paciente) ? null : paciente;
            if (nuevo == d.Sesion.Paciente) return Results.Redirect($"/sesiones/{id}?ok=paciente");
            if (!await repo.CambiarPacienteAsync(id, nuevo)) return Results.Redirect($"/sesiones/{id}?error=paciente");
            await log.RegistrarAsync(u, "cambiar_paciente_sesion",
                $"Sesión {d.Sesion.Codigo}: {d.Sesion.Paciente ?? "sin paciente"} → {nuevo ?? "sin paciente"}");
            return Results.Redirect($"/sesiones/{id}?ok=paciente");
        }).ExigirToken();

        app.MapPost("/sesiones/{id}/mediciones", async (ulong id, HttpRequest req,
            Mediciones repo, Sesiones sesiones, ClaimsPrincipal u, Auditoria log) =>
        {
            var form = await req.ReadFormAsync();
            string destino = Destino(form, id);
            if (await repo.GuardarAsync(id, form, u.Identity!.Name!) is not null)
                return Results.Redirect($"{destino}?error=mediciones&s={id}#s{id}");
            await log.RegistrarAsync(u, "mediciones_sesion", await sesiones.NombreAsync(id));
            return Results.Redirect($"{destino}?ok=mediciones&s={id}#s{id}");
        }).ExigirToken();

        app.MapPost("/sesiones/{id}/mediciones/borrar", async (ulong id, HttpRequest req,
            Mediciones repo, Sesiones sesiones, ClaimsPrincipal u, Auditoria log) =>
        {
            var form = await req.ReadFormAsync();
            if (await repo.BorrarAsync(id)) await log.RegistrarAsync(u, "borrar_mediciones", await sesiones.NombreAsync(id));
            return Results.Redirect($"{Destino(form, id)}?ok=mediciones_borradas&s={id}#s{id}");
        }).ExigirToken();

        app.MapPost("/sesiones/{id}/observacion", async (ulong id, [FromForm] string? texto,
            Observaciones repo, Sesiones sesiones, ClaimsPrincipal u, Auditoria log) =>
        {
            if (!await repo.GuardarAsync(id, texto, u.Identity!.Name!))
                return Results.Redirect($"/sesiones/{id}?error=observacion");
            await log.RegistrarAsync(u, "observacion_sesion", await sesiones.NombreAsync(id));
            return Results.Redirect($"/sesiones/{id}?ok=observacion");
        }).ExigirToken();

        app.MapPost("/sesiones/{id}/eliminar", async (ulong id, [FromForm] string? confirmar, Sesiones repo, ClaimsPrincipal u, Auditoria log) =>
        {
            if ((await repo.BuscarAsync(id))?.Sesion is not { } s) return Results.Redirect("/sesiones");
            if (!string.Equals(confirmar?.Trim(), s.Codigo, StringComparison.OrdinalIgnoreCase))
                return Results.Redirect($"/sesiones/{id}?error=confirmar#eliminar");
            if (!await repo.EliminarAsync(id)) return Results.Redirect($"/sesiones/{id}?error=eliminar#eliminar");
            await log.RegistrarAsync(u, "eliminar_sesion", $"Sesión {s.Codigo}");
            return Results.Redirect(s.Paciente is null ? "/sesiones?eliminada=1" : $"/pacientes/{s.Paciente}?ok=sesion_eliminada");
        }).ExigirToken();

        app.MapPost("/sesiones/{id}/observacion/borrar", async (ulong id, Observaciones repo, Sesiones sesiones, ClaimsPrincipal u, Auditoria log) =>
        {
            if (await repo.BorrarAsync(id)) await log.RegistrarAsync(u, "borrar_observacion", await sesiones.NombreAsync(id));
            return Results.Redirect($"/sesiones/{id}?ok=borrada");
        }).ExigirToken();
    }

    // volver: código de paciente validado; evita redirección abierta
    private static string Destino(IFormCollection form, ulong id)
    {
        string volver = form["volver"].ToString();
        return Regex.IsMatch(volver, "^P-[0-9]{3,6}$") ? $"/pacientes/{volver}" : $"/sesiones/{id}";
    }
}
