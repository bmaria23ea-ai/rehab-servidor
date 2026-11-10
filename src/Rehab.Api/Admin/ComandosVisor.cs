// Comandos de administración de visores. Solo se ejecutan desde la terminal del servidor:
//   docker compose exec api dotnet Rehab.Api.dll visor-aprobar K7QM-4XPA "Quest 3 - 01"
// El código lo muestran las gafas al pulsar "Vincular visor"; aprobarlo crea el visor.
using System.Text.RegularExpressions;
using MySqlConnector;
using Rehab.Api.Seguridad;

namespace Rehab.Api.Admin;

public static partial class ComandosVisor
{
    [GeneratedRegex(@"^[\p{L}\p{N} ._-]{1,40}$")]
    private static partial Regex NombreValido();

    public static async Task<int> EjecutarAsync(string[] args, MySqlDataSource ds)
    {
        ushort id = 0;
        bool tieneId = args.Length == 2 && ushort.TryParse(args[1], out id) && id > 0;

        try
        {
            return args[0] switch
            {
                "visor-aprobar" when args.Length == 3 => await AprobarAsync(ds, args[1], args[2]),
                "visor-revocar" when tieneId => await RevocarAsync(ds, id),
                "visor-lista" when args.Length == 1 => await ListaAsync(ds),
                _ => Ayuda(),
            };
        }
        catch (MySqlException ex) when (ex.Number == 1062)   // nombre duplicado
        {
            Console.Error.WriteLine("Ya existe un visor con ese nombre.");
            return 1;
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"Error de base de datos ({ex.Number}).");
            return 1;
        }
    }

    // Aprueba la solicitud que muestran las gafas: crea el visor (pendiente) y lo une a la solicitud.
    // Las gafas lo detectan en su siguiente consulta, reciben su secreto y el visor pasa a "activo".
    private static async Task<int> AprobarAsync(MySqlDataSource ds, string codigoTexto, string nombre)
    {
        string? codigo = CodigosEnrolamiento.Normalizar(codigoTexto);
        if (codigo is null)
        {
            Console.Error.WriteLine("Código no válido. Escríbalo como aparece en las gafas, por ejemplo K7QM-4XPA.");
            return 1;
        }
        nombre = nombre.Trim();
        if (!NombreValido().IsMatch(nombre))
        {
            Console.Error.WriteLine("Nombre no válido: de 1 a 40 letras, números, espacios, punto, guion o guion bajo.");
            return 1;
        }

        await using var cn = await ds.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();

        await using var buscar = new MySqlCommand(
            "SELECT id FROM solicitudes_vinculacion " +
            "WHERE codigo_hash = @hash AND visor_id IS NULL AND expira_utc > UTC_TIMESTAMP(3) FOR UPDATE", cn, tx);
        buscar.Parameters.AddWithValue("@hash", CodigosEnrolamiento.Hash(codigo));
        object? solicitud = await buscar.ExecuteScalarAsync();
        if (solicitud is null)
        {
            Console.Error.WriteLine("No hay ninguna solicitud vigente con ese código. Pulse de nuevo \"Vincular visor\" en las gafas.");
            return 1;
        }

        await using var crear = new MySqlCommand("INSERT INTO visores (nombre) VALUES (@nombre)", cn, tx);
        crear.Parameters.AddWithValue("@nombre", nombre);
        await crear.ExecuteNonQueryAsync();
        var id = (ushort)crear.LastInsertedId;

        await using var unir = new MySqlCommand(
            "UPDATE solicitudes_vinculacion SET visor_id = @visor WHERE id = @id", cn, tx);
        unir.Parameters.AddWithValue("@visor", id);
        unir.Parameters.AddWithValue("@id", solicitud);
        await unir.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        Console.WriteLine($"Visor #{id} \"{nombre}\" aprobado. Las gafas se vincularán en unos segundos.");
        return 0;
    }

    // Revoca un visor: borra su secreto y su solicitud pendiente. Queda bloqueado de inmediato.
    private static async Task<int> RevocarAsync(MySqlDataSource ds, ushort id)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();

        await using var cmd = new MySqlCommand(
            "UPDATE visores SET estado = 'revocado', secreto_hash = NULL, revocado_utc = UTC_TIMESTAMP(3) " +
            "WHERE id = @id AND estado <> 'revocado'", cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        int filas = await cmd.ExecuteNonQueryAsync();

        if (filas == 0) { Console.Error.WriteLine($"El visor #{id} no existe o ya estaba revocado."); return 1; }

        await using var borrar = new MySqlCommand(
            "DELETE FROM solicitudes_vinculacion WHERE visor_id = @id", cn, tx);
        borrar.Parameters.AddWithValue("@id", id);
        await borrar.ExecuteNonQueryAsync();

        await tx.CommitAsync();
        Console.WriteLine($"Visor #{id} revocado. Ya no puede enviar datos.");
        return 0;
    }

    // Lista los visores con su estado
    private static async Task<int> ListaAsync(MySqlDataSource ds)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT id, nombre, estado, enrolado_utc, ultimo_acceso_utc FROM visores ORDER BY id", cn);
        await using var r = await cmd.ExecuteReaderAsync();

        Console.WriteLine($"{"#",-4} {"Nombre",-40} {"Estado",-10} {"Enrolado (UTC)",-17} {"Último acceso (UTC)",-17}");
        while (await r.ReadAsync())
        {
            string enrolado = r.IsDBNull(3) ? "-" : r.GetDateTime(3).ToString("yyyy-MM-dd HH:mm");
            string acceso = r.IsDBNull(4) ? "-" : r.GetDateTime(4).ToString("yyyy-MM-dd HH:mm");
            Console.WriteLine($"{r.GetUInt16(0),-4} {r.GetString(1),-40} {r.GetString(2),-10} {enrolado,-17} {acceso,-17}");
        }
        return 0;
    }

    private static int Ayuda()
    {
        Console.Error.WriteLine("""
            Comandos:
              visor-aprobar <código> "<nombre>"   Vincula las gafas que muestran ese código
              visor-revocar <id>                  Revoca un visor (queda bloqueado de inmediato)
              visor-lista                         Muestra los visores y su estado
            """);
        return 2;
    }
}
