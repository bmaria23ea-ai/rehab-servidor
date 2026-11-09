// GET /asignacion: el visor consulta qué paciente tiene asignado (requiere JWT de visor).
// 200 con el código si hay una asignación vigente; 204 si no hay.
using MySqlConnector;
using Rehab.Api.Seguridad;

namespace Rehab.Api.Endpoints;

public static class AsignacionEndpoints
{
    public sealed record RespuestaAsignacion(string Paciente, bool EsPrueba, DateTime ExpiraUtc);

    public static void MapAsignacion(this IEndpointRouteBuilder app)
    {
        app.MapGet("/asignacion", ConsultarAsync).RequireAuthorization();
    }

    private static async Task<IResult> ConsultarAsync(HttpContext http, MySqlDataSource ds, CancellationToken ct)
    {
        ushort visorId = ushort.Parse(http.User.FindFirst(EmisorJwt.ClaimVisor)!.Value);

        await using var cn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(
            "SELECT p.codigo, p.es_prueba, a.expira_utc FROM asignaciones a " +
            "JOIN pacientes p ON p.id = a.paciente_id " +
            "WHERE a.visor_id = @visor AND a.expira_utc > UTC_TIMESTAMP(3)", cn);
        cmd.Parameters.AddWithValue("@visor", visorId);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        if (!await r.ReadAsync(ct))
            return Results.NoContent();

        var expira = DateTime.SpecifyKind(r.GetDateTime(2), DateTimeKind.Utc);
        return Results.Ok(new RespuestaAsignacion(r.GetString(0), r.GetBoolean(1), expira));
    }
}
