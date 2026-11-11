using MySqlConnector;

namespace Rehab.Portal.Datos;

public sealed record Observacion(string Texto, string Terapeuta, DateTime Actualizado);

public sealed class Observaciones(MySqlDataSource ds)
{
    public const int Maximo = 1000;

    public async Task<Observacion?> BuscarAsync(ulong sesionId)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT texto, terapeuta, actualizado_utc FROM sesion_observaciones WHERE sesion_id = @id", cn);
        cmd.Parameters.AddWithValue("@id", sesionId);
        await using var r = await cmd.ExecuteReaderAsync();
        return await r.ReadAsync() ? new Observacion(r.GetString(0), r.GetString(1), r.GetDateTime(2)) : null;
    }

    // false si la sesión no existe, el texto está vacío o es muy largo
    public async Task<bool> GuardarAsync(ulong sesionId, string? texto, string terapeuta)
    {
        texto = texto?.Trim() ?? "";
        if (texto.Length is 0 or > Maximo) return false;

        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO sesion_observaciones (sesion_id, texto, terapeuta) " +
            "SELECT id, @texto, @terapeuta FROM sesiones WHERE id = @id " +
            "ON DUPLICATE KEY UPDATE texto = VALUES(texto), terapeuta = VALUES(terapeuta), actualizado_utc = UTC_TIMESTAMP(3)", cn);
        cmd.Parameters.AddWithValue("@id", sesionId);
        cmd.Parameters.AddWithValue("@texto", texto);
        cmd.Parameters.AddWithValue("@terapeuta", terapeuta);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> BorrarAsync(ulong sesionId)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("DELETE FROM sesion_observaciones WHERE sesion_id = @id", cn);
        cmd.Parameters.AddWithValue("@id", sesionId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
