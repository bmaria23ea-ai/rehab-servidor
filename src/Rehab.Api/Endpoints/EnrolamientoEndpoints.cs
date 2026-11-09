// POST /visores/enrolar: un visor canjea su código de enrolamiento por un secreto propio.
// El secreto se entrega una sola vez; en la base solo queda su hash SHA-256.
using System.Buffers.Text;
using System.Security.Cryptography;
using MySqlConnector;
using Rehab.Api.Seguridad;

namespace Rehab.Api.Endpoints;

public static class EnrolamientoEndpoints
{
    public sealed record SolicitudEnrolamiento(string? Codigo);
    public sealed record RespuestaEnrolamiento(int VisorId, string Secreto);

    public static void MapEnrolamiento(this IEndpointRouteBuilder app)
    {
        app.MapPost("/visores/enrolar", EnrolarAsync)
           .RequireRateLimiting(Limites.Enrolamiento);
    }

    private static async Task<IResult> EnrolarAsync(
        SolicitudEnrolamiento solicitud, MySqlDataSource ds, ILogger<Program> log, CancellationToken ct)
    {
        string? codigo = CodigosEnrolamiento.Normalizar(solicitud.Codigo);
        if (codigo is null)
            return Results.Problem(statusCode: 400, title: "Formato de código no válido.");

        byte[] codigoHash = CodigosEnrolamiento.Hash(codigo);

        await using var cn = await ds.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        // Código vigente, sin usar y de un visor pendiente (se bloquea la fila hasta terminar)
        await using var buscar = new MySqlCommand(
            "SELECT c.id, c.visor_id FROM codigos_enrolamiento c " +
            "JOIN visores v ON v.id = c.visor_id " +
            "WHERE c.codigo_hash = @hash AND c.usado_utc IS NULL " +
            "AND c.expira_utc > UTC_TIMESTAMP(3) AND v.estado = 'pendiente' " +
            "FOR UPDATE", cn, tx);
        buscar.Parameters.AddWithValue("@hash", codigoHash);

        int codigoId, visorId;
        await using (var r = await buscar.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct))
            {
                // Mismo mensaje para código inexistente, vencido o usado: no da pistas
                log.LogWarning("Intento de enrolamiento rechazado");
                return Results.Problem(statusCode: 401, title: "Código no válido o vencido.");
            }
            codigoId = Convert.ToInt32(r.GetValue(0));
            visorId = r.GetUInt16(1);
        }

        // Secreto aleatorio de 256 bits; se guarda solo su hash
        byte[] secreto = RandomNumberGenerator.GetBytes(32);
        byte[] secretoHash = SHA256.HashData(secreto);

        await using (var usar = new MySqlCommand(
            "UPDATE codigos_enrolamiento SET usado_utc = UTC_TIMESTAMP(3) WHERE id = @id", cn, tx))
        {
            usar.Parameters.AddWithValue("@id", codigoId);
            await usar.ExecuteNonQueryAsync(ct);
        }

        await using (var activar = new MySqlCommand(
            "UPDATE visores SET estado = 'activo', secreto_hash = @hash, enrolado_utc = UTC_TIMESTAMP(3) " +
            "WHERE id = @id AND estado = 'pendiente'", cn, tx))
        {
            activar.Parameters.AddWithValue("@hash", secretoHash);
            activar.Parameters.AddWithValue("@id", visorId);
            await activar.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        log.LogInformation("Visor {VisorId} enrolado", visorId);

        return Results.Ok(new RespuestaEnrolamiento(visorId, Base64Url.EncodeToString(secreto)));
    }
}
