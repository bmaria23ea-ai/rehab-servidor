// POST /token: el visor presenta su id y su secreto y recibe un JWT de 15 minutos.
// GET /visores/yo: prueba de autenticación; responde el id del visor del token.
using System.Buffers.Text;
using System.Security.Claims;
using System.Security.Cryptography;
using MySqlConnector;
using Rehab.Api.Seguridad;

namespace Rehab.Api.Endpoints;

public static class TokenEndpoints
{
    public sealed record SolicitudToken(int VisorId, string? Secreto);
    public sealed record RespuestaToken(string AccessToken, int ExpiraEn);

    public static void MapToken(this IEndpointRouteBuilder app)
    {
        app.MapPost("/token", EmitirAsync)
           .RequireRateLimiting(Limites.Token);

        app.MapGet("/visores/yo", (ClaimsPrincipal usuario) =>
                Results.Ok(new { visorId = int.Parse(usuario.FindFirst(EmisorJwt.ClaimVisor)!.Value) }))
           .RequireAuthorization();
    }

    private static async Task<IResult> EmitirAsync(
        SolicitudToken solicitud, MySqlDataSource ds, EmisorJwt emisor, ILogger<Program> log, CancellationToken ct)
    {
        byte[]? secreto = DecodificarSecreto(solicitud.Secreto);
        if (solicitud.VisorId is <= 0 or > ushort.MaxValue || secreto is null)
            return Results.Problem(statusCode: 400, title: "Solicitud no válida.");

        byte[] hashRecibido = SHA256.HashData(secreto);

        await using var cn = await ds.OpenConnectionAsync(ct);
        await using var buscar = new MySqlCommand(
            "SELECT secreto_hash FROM visores WHERE id = @id AND estado = 'activo'", cn);
        buscar.Parameters.AddWithValue("@id", solicitud.VisorId);
        var guardado = await buscar.ExecuteScalarAsync(ct) as byte[];

        // Comparación en tiempo constante; si el visor no existe se compara igual (contra ceros)
        bool coincide = CryptographicOperations.FixedTimeEquals(hashRecibido, guardado ?? new byte[32]);
        if (guardado is null || !coincide)
        {
            log.LogWarning("Solicitud de token rechazada para el visor {VisorId}", solicitud.VisorId);
            return Results.Problem(statusCode: 401, title: "Credenciales no válidas.");
        }

        await using (var acceso = new MySqlCommand(
            "UPDATE visores SET ultimo_acceso_utc = UTC_TIMESTAMP(3) WHERE id = @id", cn))
        {
            acceso.Parameters.AddWithValue("@id", solicitud.VisorId);
            await acceso.ExecuteNonQueryAsync(ct);
        }

        return Results.Ok(new RespuestaToken(emisor.Emitir(solicitud.VisorId), (int)EmisorJwt.Duracion.TotalSeconds));
    }

    // El secreto son 32 bytes en Base64Url (43 caracteres)
    private static byte[]? DecodificarSecreto(string? texto)
    {
        if (texto is null || texto.Length != 43) return null;
        try
        {
            byte[] bytes = Base64Url.DecodeFromChars(texto);
            return bytes.Length == 32 ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
