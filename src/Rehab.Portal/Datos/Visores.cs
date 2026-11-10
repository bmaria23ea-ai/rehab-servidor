using System.Text.RegularExpressions;
using MySqlConnector;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Datos;

public sealed record VisorFila(ushort Id, string Nombre, string Estado, DateTime? UltimoAcceso, string? Paciente, DateTime? Expira);

public sealed partial class Visores(MySqlDataSource ds)
{
    // Límite de seguridad: si nadie cambia o quita al paciente, la asignación se borra sola.
    public const int HorasAsignacion = 4;

    [GeneratedRegex(@"^[\p{L}\p{N} ._-]{1,40}$")]
    private static partial Regex NombreValido();

    public async Task<List<VisorFila>> ListarAsync()
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT v.id, v.nombre, v.estado, v.ultimo_acceso_utc, p.codigo, a.expira_utc " +
            "FROM visores v " +
            "LEFT JOIN asignaciones a ON a.visor_id = v.id AND a.expira_utc > UTC_TIMESTAMP(3) " +
            "LEFT JOIN pacientes p ON p.id = a.paciente_id " +
            "WHERE v.estado <> 'revocado' ORDER BY v.nombre", cn);
        await using var r = await cmd.ExecuteReaderAsync();

        var lista = new List<VisorFila>();
        while (await r.ReadAsync())
            lista.Add(new VisorFila(r.GetUInt16(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetDateTime(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDateTime(5)));
        return lista;
    }

    public async Task<List<string>> CodigosPacientesAsync()
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("SELECT codigo FROM pacientes ORDER BY es_prueba, codigo", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        var lista = new List<string>();
        while (await r.ReadAsync()) lista.Add(r.GetString(0));
        return lista;
    }

    // Igual que visor-aprobar: crea el visor y lo une a la solicitud que muestran las gafas.
    public async Task<string?> AprobarAsync(string codigoTexto, string nombre)
    {
        string? codigo = CodigosVinculacion.Normalizar(codigoTexto);
        nombre = nombre.Trim();
        if (codigo is null) return "codigo";
        if (!NombreValido().IsMatch(nombre)) return "nombre";

        await using var cn = await ds.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();

        await using var buscar = new MySqlCommand(
            "SELECT id FROM solicitudes_vinculacion " +
            "WHERE codigo_hash = @hash AND visor_id IS NULL AND expira_utc > UTC_TIMESTAMP(3) FOR UPDATE", cn, tx);
        buscar.Parameters.AddWithValue("@hash", CodigosVinculacion.Hash(codigo));
        if (await buscar.ExecuteScalarAsync() is not { } solicitud) return "solicitud";

        await using var crear = new MySqlCommand("INSERT INTO visores (nombre) VALUES (@nombre)", cn, tx);
        crear.Parameters.AddWithValue("@nombre", nombre);
        try
        {
            await crear.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return "duplicado";
        }

        await using var unir = new MySqlCommand(
            "UPDATE solicitudes_vinculacion SET visor_id = @visor WHERE id = @id", cn, tx);
        unir.Parameters.AddWithValue("@visor", crear.LastInsertedId);
        unir.Parameters.AddWithValue("@id", solicitud);
        await unir.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        return null;
    }

    public async Task<string?> AsignarAsync(ushort visorId, string codigoPaciente)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO asignaciones (visor_id, paciente_id, expira_utc) " +
            "SELECT v.id, p.id, UTC_TIMESTAMP(3) + INTERVAL @horas HOUR " +
            "FROM visores v JOIN pacientes p ON p.codigo = @paciente " +
            "WHERE v.id = @visor AND v.estado = 'activo' " +
            "ON DUPLICATE KEY UPDATE paciente_id = VALUES(paciente_id), creada_utc = UTC_TIMESTAMP(3), " +
            "expira_utc = VALUES(expira_utc)", cn);
        cmd.Parameters.AddWithValue("@horas", HorasAsignacion);
        cmd.Parameters.AddWithValue("@paciente", codigoPaciente);
        cmd.Parameters.AddWithValue("@visor", visorId);
        return await cmd.ExecuteNonQueryAsync() > 0 ? null : "asignar";
    }

    public async Task QuitarAsync(ushort visorId)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("DELETE FROM asignaciones WHERE visor_id = @visor", cn);
        cmd.Parameters.AddWithValue("@visor", visorId);
        await cmd.ExecuteNonQueryAsync();
    }

    // Igual que visor-revocar: bloqueado de inmediato (la API lo comprueba en cada petición).
    public async Task RevocarAsync(ushort visorId)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();
        foreach (var sql in new[]
        {
            "UPDATE visores SET estado = 'revocado', secreto_hash = NULL, revocado_utc = UTC_TIMESTAMP(3) WHERE id = @id AND estado <> 'revocado'",
            "DELETE FROM solicitudes_vinculacion WHERE visor_id = @id",
            "DELETE FROM asignaciones WHERE visor_id = @id",
        })
        {
            await using var cmd = new MySqlCommand(sql, cn, tx);
            cmd.Parameters.AddWithValue("@id", visorId);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }
}
