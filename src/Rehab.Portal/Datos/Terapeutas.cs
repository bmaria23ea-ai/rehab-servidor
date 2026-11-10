using MySqlConnector;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Datos;

public sealed record Terapeuta(ushort Id, string Usuario, string Hash, bool DebeCambiar, string Sello, bool Activo, DateTime? BloqueadoHasta)
{
    public bool Bloqueado => BloqueadoHasta > DateTime.UtcNow;
}

public sealed class Terapeutas(MySqlDataSource ds)
{
    public const int MaxIntentos = 5;
    public const int MinutosBloqueo = 15;

    public async Task<Terapeuta?> BuscarAsync(string usuario)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, usuario, password_hash, debe_cambiar, sello, activo, bloqueado_hasta " +
            "FROM terapeutas WHERE usuario = @usuario", cn);
        cmd.Parameters.AddWithValue("@usuario", usuario);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new Terapeuta(r.GetUInt16(0), r.GetString(1), r.GetString(2), r.GetBoolean(3), r.GetString(4),
                             r.GetBoolean(5), r.IsDBNull(6) ? null : r.GetDateTime(6));
    }

    // El bloqueo se evalúa antes de sumar; al bloquear, el contador vuelve a 0.
    public Task FalloAsync(ushort id) => EjecutarAsync(
        "UPDATE terapeutas SET " +
        $"bloqueado_hasta = IF(intentos_fallidos + 1 >= {MaxIntentos}, UTC_TIMESTAMP(3) + INTERVAL {MinutosBloqueo} MINUTE, bloqueado_hasta), " +
        $"intentos_fallidos = IF(intentos_fallidos + 1 >= {MaxIntentos}, 0, intentos_fallidos + 1) " +
        "WHERE id = @id", id);

    public Task AccesoAsync(ushort id) => EjecutarAsync(
        "UPDATE terapeutas SET intentos_fallidos = 0, bloqueado_hasta = NULL, ultimo_acceso_utc = UTC_TIMESTAMP(3) " +
        "WHERE id = @id", id);

    public async Task<string> CambiarClaveAsync(ushort id, string nueva)
    {
        string sello = Contrasenas.NuevoSello();
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE terapeutas SET password_hash = @hash, debe_cambiar = FALSE, sello = @sello WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("@hash", Contrasenas.Hash(nueva));
        cmd.Parameters.AddWithValue("@sello", sello);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
        return sello;
    }

    public async Task<bool> SesionVigenteAsync(ushort id, string sello)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT 1 FROM terapeutas WHERE id = @id AND sello = @sello AND activo", cn);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@sello", sello);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    private async Task EjecutarAsync(string sql, ushort id)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync();
    }
}
