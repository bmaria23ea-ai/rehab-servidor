// GET /pacientes: lista de códigos para el menú del visor (requiere JWT de visor).
// Solo códigos seudónimos: nunca nombres ni datos personales.
// Aparecen los pacientes de prueba y los reales con consentimiento registrado.
using MySqlConnector;

namespace Rehab.Api.Endpoints;

public static class PacientesEndpoints
{
    public sealed record PacienteMenu(string Codigo, bool EsPrueba);

    public static void MapPacientes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/pacientes", ListarAsync).RequireAuthorization();
    }

    private static async Task<IResult> ListarAsync(MySqlDataSource ds, CancellationToken ct)
    {
        await using var cn = await ds.OpenConnectionAsync(ct);
        await using var cmd = new MySqlCommand(
            "SELECT codigo, es_prueba FROM pacientes " +
            "WHERE es_prueba OR consentimiento_utc IS NOT NULL ORDER BY codigo", cn);
        await using var r = await cmd.ExecuteReaderAsync(ct);

        var lista = new List<PacienteMenu>();
        while (await r.ReadAsync(ct))
            lista.Add(new PacienteMenu(r.GetString(0), r.GetBoolean(1)));

        return Results.Ok(lista);
    }
}
