// Comandos de administración de visores. Solo se ejecutan desde la terminal del servidor:
//   docker compose exec api dotnet Rehab.Api.dll visor-nuevo "Quest 3 - 01"
// El código de enrolamiento se muestra solo en esa terminal; nunca se escribe en los registros.
using System.Text.RegularExpressions;
using MySqlConnector;
using Rehab.Api.Seguridad;

namespace Rehab.Api.Admin;

public static partial class ComandosVisor
{
    private const int MinutosVigencia = 10;

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
                "visor-nuevo" when args.Length == 2 => await NuevoAsync(ds, args[1]),
                "visor-codigo" when tieneId => await NuevoCodigoAsync(ds, id),
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

    // Registra un visor (estado "pendiente") y genera su código de enrolamiento
    private static async Task<int> NuevoAsync(MySqlDataSource ds, string nombre)
    {
        nombre = nombre.Trim();
        if (!NombreValido().IsMatch(nombre))
        {
            Console.Error.WriteLine("Nombre no válido: de 1 a 40 letras, números, espacios, punto, guion o guion bajo.");
            return 1;
        }

        await using var cn = await ds.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();

        await using var cmd = new MySqlCommand("INSERT INTO visores (nombre) VALUES (@nombre)", cn, tx);
        cmd.Parameters.AddWithValue("@nombre", nombre);
        await cmd.ExecuteNonQueryAsync();
        var id = (ushort)cmd.LastInsertedId;

        string codigo = await CrearCodigoAsync(cn, tx, id);
        await tx.CommitAsync();

        Console.WriteLine($"Visor creado: #{id} \"{nombre}\"");
        MostrarCodigo(codigo);
        return 0;
    }

    // Genera un código nuevo para un visor pendiente (por ejemplo, si el anterior venció)
    private static async Task<int> NuevoCodigoAsync(MySqlDataSource ds, ushort id)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();

        await using var cmd = new MySqlCommand("SELECT estado FROM visores WHERE id = @id FOR UPDATE", cn, tx);
        cmd.Parameters.AddWithValue("@id", id);
        var estado = await cmd.ExecuteScalarAsync() as string;

        if (estado is null) { Console.Error.WriteLine($"No existe el visor #{id}."); return 1; }
        if (estado != "pendiente")
        {
            Console.Error.WriteLine($"El visor #{id} está \"{estado}\". Solo se generan códigos para visores pendientes.");
            return 1;
        }

        string codigo = await CrearCodigoAsync(cn, tx, id);
        await tx.CommitAsync();

        Console.WriteLine($"Nuevo código para el visor #{id}");
        MostrarCodigo(codigo);
        return 0;
    }

    // Revoca un visor: borra su secreto y sus códigos sin usar. Queda bloqueado de inmediato.
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
            "DELETE FROM codigos_enrolamiento WHERE visor_id = @id AND usado_utc IS NULL", cn, tx);
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

    // Borra los códigos anteriores sin usar del visor y guarda uno nuevo (solo su hash)
    private static async Task<string> CrearCodigoAsync(MySqlConnection cn, MySqlTransaction tx, ushort visorId)
    {
        await using (var borrar = new MySqlCommand(
            "DELETE FROM codigos_enrolamiento WHERE visor_id = @id AND usado_utc IS NULL", cn, tx))
        {
            borrar.Parameters.AddWithValue("@id", visorId);
            await borrar.ExecuteNonQueryAsync();
        }

        string codigo = CodigosEnrolamiento.Nuevo();
        await using var cmd = new MySqlCommand(
            "INSERT INTO codigos_enrolamiento (visor_id, codigo_hash, expira_utc) " +
            $"VALUES (@id, @hash, UTC_TIMESTAMP(3) + INTERVAL {MinutosVigencia} MINUTE)", cn, tx);
        cmd.Parameters.AddWithValue("@id", visorId);
        cmd.Parameters.AddWithValue("@hash", CodigosEnrolamiento.Hash(codigo));
        await cmd.ExecuteNonQueryAsync();
        return codigo;
    }

    private static void MostrarCodigo(string codigo)
    {
        Console.WriteLine($"Código de enrolamiento: {CodigosEnrolamiento.Formatear(codigo)}");
        Console.WriteLine($"Vence en {MinutosVigencia} minutos y sirve una sola vez. No lo guardes ni lo compartas por chat.");
    }

    private static int Ayuda()
    {
        Console.Error.WriteLine("""
            Comandos:
              visor-nuevo "<nombre>"   Registra un visor y muestra su código de enrolamiento
              visor-codigo <id>        Genera un código nuevo para un visor pendiente
              visor-revocar <id>       Revoca un visor (queda bloqueado de inmediato)
              visor-lista              Muestra los visores y su estado
            """);
        return 2;
    }
}
