// Vinculación de visores sin escribir en las gafas (mismo esquema que RFC 8628, "device authorization grant"):
//   1. POST /visores/solicitar: las gafas reciben un código corto para mostrar y un identificador secreto.
//   2. El administrador aprueba el código en el servidor: visor-aprobar <código> "<nombre>".
//   3. POST /visores/consultar: las gafas preguntan cada pocos segundos; al aprobarse reciben su secreto.
// En la base solo se guardan hashes; el secreto del visor se entrega una sola vez.
using System.Buffers.Text;
using System.Security.Cryptography;
using MySqlConnector;
using Rehab.Api.Seguridad;

namespace Rehab.Api.Endpoints;

public static class VinculacionEndpoints
{
    public const int MinutosVigencia = 10;
    private const int MaximoSolicitudesActivas = 20;   // evita que se llene la tabla con solicitudes falsas
    private const int SegundosEntreConsultas = 5;

    public sealed record RespuestaSolicitud(string CodigoDispositivo, string Codigo, int ExpiraEn, int Intervalo);
    public sealed record SolicitudConsulta(string? CodigoDispositivo);
    public sealed record RespuestaVinculado(int VisorId, string Secreto);

    public static void MapVinculacion(this IEndpointRouteBuilder app)
    {
        app.MapPost("/visores/solicitar", SolicitarAsync)
           .RequireRateLimiting(Limites.Enrolamiento);

        app.MapPost("/visores/consultar", ConsultarAsync)
           .RequireRateLimiting(Limites.Consulta);
    }

    private static async Task<IResult> SolicitarAsync(MySqlDataSource ds, ILogger<Program> log, CancellationToken ct)
    {
        await using var cn = await ds.OpenConnectionAsync(ct);

        // Limpieza: las solicitudes vencidas ya no sirven
        await using (var limpiar = new MySqlCommand(
            "DELETE FROM solicitudes_vinculacion WHERE expira_utc <= UTC_TIMESTAMP(3)", cn))
            await limpiar.ExecuteNonQueryAsync(ct);

        await using (var contar = new MySqlCommand("SELECT COUNT(*) FROM solicitudes_vinculacion", cn))
        {
            if (Convert.ToInt32(await contar.ExecuteScalarAsync(ct)) >= MaximoSolicitudesActivas)
            {
                log.LogWarning("Demasiadas solicitudes de vinculación activas");
                return Results.Problem(statusCode: 503, title: "Intente de nuevo en unos minutos.");
            }
        }

        byte[] dispositivo = RandomNumberGenerator.GetBytes(32);   // 256 bits, solo lo conocen las gafas
        string codigo = CodigosEnrolamiento.Nuevo();

        await using var insertar = new MySqlCommand(
            "INSERT INTO solicitudes_vinculacion (codigo_hash, dispositivo_hash, expira_utc) " +
            $"VALUES (@codigo, @dispositivo, UTC_TIMESTAMP(3) + INTERVAL {MinutosVigencia} MINUTE)", cn);
        insertar.Parameters.AddWithValue("@codigo", CodigosEnrolamiento.Hash(codigo));
        insertar.Parameters.AddWithValue("@dispositivo", SHA256.HashData(dispositivo));
        try
        {
            await insertar.ExecuteNonQueryAsync(ct);
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            // Código repetido (muy improbable): las gafas simplemente piden otro
            return Results.Problem(statusCode: 503, title: "Intente de nuevo.");
        }

        log.LogInformation("Solicitud de vinculación creada");
        return Results.Ok(new RespuestaSolicitud(
            Base64Url.EncodeToString(dispositivo), CodigosEnrolamiento.Formatear(codigo),
            MinutosVigencia * 60, SegundosEntreConsultas));
    }

    private static async Task<IResult> ConsultarAsync(
        SolicitudConsulta consulta, MySqlDataSource ds, ILogger<Program> log, CancellationToken ct)
    {
        byte[]? dispositivo = Decodificar32(consulta.CodigoDispositivo);
        if (dispositivo is null)
            return Results.Problem(statusCode: 400, title: "Solicitud no válida.");

        await using var cn = await ds.OpenConnectionAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        await using var buscar = new MySqlCommand(
            "SELECT id, visor_id FROM solicitudes_vinculacion " +
            "WHERE dispositivo_hash = @hash AND expira_utc > UTC_TIMESTAMP(3) FOR UPDATE", cn, tx);
        buscar.Parameters.AddWithValue("@hash", SHA256.HashData(dispositivo));

        int solicitudId;
        ushort? visorId;
        await using (var r = await buscar.ExecuteReaderAsync(ct))
        {
            if (!await r.ReadAsync(ct))
                return Results.Problem(statusCode: 401, title: "Solicitud no válida o vencida.");
            solicitudId = Convert.ToInt32(r.GetValue(0));
            visorId = r.IsDBNull(1) ? null : r.GetUInt16(1);
        }

        if (visorId is null)
            return Results.Accepted();   // 202: todavía no se aprueba

        // Aprobada: se genera el secreto del visor, se activa y la solicitud se borra (un solo uso)
        byte[] secreto = RandomNumberGenerator.GetBytes(32);

        await using (var activar = new MySqlCommand(
            "UPDATE visores SET estado = 'activo', secreto_hash = @hash, enrolado_utc = UTC_TIMESTAMP(3) " +
            "WHERE id = @id AND estado = 'pendiente'", cn, tx))
        {
            activar.Parameters.AddWithValue("@hash", SHA256.HashData(secreto));
            activar.Parameters.AddWithValue("@id", visorId.Value);
            if (await activar.ExecuteNonQueryAsync(ct) != 1)
                return Results.Problem(statusCode: 401, title: "Solicitud no válida o vencida.");
        }

        await using (var borrar = new MySqlCommand("DELETE FROM solicitudes_vinculacion WHERE id = @id", cn, tx))
        {
            borrar.Parameters.AddWithValue("@id", solicitudId);
            await borrar.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        log.LogInformation("Visor {VisorId} vinculado", visorId.Value);
        return Results.Ok(new RespuestaVinculado(visorId.Value, Base64Url.EncodeToString(secreto)));
    }

    // 32 bytes en Base64Url (43 caracteres)
    private static byte[]? Decodificar32(string? texto)
    {
        if (texto is null || texto.Length != 43) return null;
        var bytes = new byte[32];
        return Base64Url.TryDecodeFromChars(texto, bytes, out int n) && n == 32 ? bytes : null;
    }
}
