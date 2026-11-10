// Cuentas de terapeutas, solo desde la terminal del servidor:
//   docker compose exec portal dotnet Rehab.Portal.dll terapeuta-nuevo mlopez
using System.Text.RegularExpressions;
using MySqlConnector;
using Rehab.Portal.Seguridad;

namespace Rehab.Portal.Admin;

public static partial class ComandosTerapeuta
{
    [GeneratedRegex("^[a-z0-9.]{3,30}$")]
    private static partial Regex UsuarioValido();

    private static readonly TimeSpan Ecuador = TimeSpan.FromHours(-5);

    public static async Task<int> EjecutarAsync(string[] args, MySqlDataSource ds)
    {
        string usuario = args.Length == 2 ? args[1].Trim().ToLowerInvariant() : "";
        if (args.Length == 2 && !UsuarioValido().IsMatch(usuario))
        {
            Console.Error.WriteLine("Usuario no válido: de 3 a 30 letras minúsculas, números o punto.");
            return 1;
        }

        try
        {
            return args[0] switch
            {
                "terapeuta-nuevo" when args.Length == 2 => await NuevoAsync(ds, usuario),
                "terapeuta-restablecer" when args.Length == 2 => await RestablecerAsync(ds, usuario),
                "terapeuta-cerrar-sesiones" when args.Length == 2 =>
                    await ActualizarAsync(ds, usuario, "sello = @sello", "Sesiones cerradas en todos sus equipos."),
                "terapeuta-desactivar" when args.Length == 2 =>
                    await ActualizarAsync(ds, usuario, "activo = FALSE, sello = @sello", "Cuenta desactivada."),
                "terapeuta-activar" when args.Length == 2 =>
                    await ActualizarAsync(ds, usuario, "activo = TRUE", "Cuenta activada."),
                "terapeuta-lista" when args.Length == 1 => await ListaAsync(ds),
                _ => Ayuda(),
            };
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            Console.Error.WriteLine("Ya existe un terapeuta con ese usuario.");
            return 1;
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"Error de base de datos ({ex.Number}).");
            return 1;
        }
    }

    private static async Task<int> NuevoAsync(MySqlDataSource ds, string usuario)
    {
        string temporal = Contrasenas.Temporal();
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO terapeutas (usuario, password_hash, sello) VALUES (@usuario, @hash, @sello)", cn);
        cmd.Parameters.AddWithValue("@usuario", usuario);
        cmd.Parameters.AddWithValue("@hash", Contrasenas.Hash(temporal));
        cmd.Parameters.AddWithValue("@sello", Contrasenas.NuevoSello());
        await cmd.ExecuteNonQueryAsync();

        MostrarTemporal(usuario, temporal);
        return 0;
    }

    private static async Task<int> RestablecerAsync(MySqlDataSource ds, string usuario)
    {
        string temporal = Contrasenas.Temporal();
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE terapeutas SET password_hash = @hash, debe_cambiar = TRUE, sello = @sello, " +
            "intentos_fallidos = 0, bloqueado_hasta = NULL WHERE usuario = @usuario", cn);
        cmd.Parameters.AddWithValue("@usuario", usuario);
        cmd.Parameters.AddWithValue("@hash", Contrasenas.Hash(temporal));
        cmd.Parameters.AddWithValue("@sello", Contrasenas.NuevoSello());
        if (await cmd.ExecuteNonQueryAsync() == 0) return NoExiste(usuario);

        MostrarTemporal(usuario, temporal);
        return 0;
    }

    private static async Task<int> ActualizarAsync(MySqlDataSource ds, string usuario, string cambios, string mensaje)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand($"UPDATE terapeutas SET {cambios} WHERE usuario = @usuario", cn);
        cmd.Parameters.AddWithValue("@usuario", usuario);
        cmd.Parameters.AddWithValue("@sello", Contrasenas.NuevoSello());
        if (await cmd.ExecuteNonQueryAsync() == 0) return NoExiste(usuario);

        Console.WriteLine($"{usuario}: {mensaje}");
        return 0;
    }

    private static async Task<int> ListaAsync(MySqlDataSource ds)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT usuario, activo, debe_cambiar, bloqueado_hasta > UTC_TIMESTAMP(3), ultimo_acceso_utc " +
            "FROM terapeutas ORDER BY usuario", cn);
        await using var r = await cmd.ExecuteReaderAsync();

        Console.WriteLine($"{"Usuario",-30} {"Estado",-10} {"Contraseña",-12} {"Último acceso (Ecuador)",-23}");
        while (await r.ReadAsync())
        {
            string estado = !r.GetBoolean(1) ? "inactivo" : !r.IsDBNull(3) && r.GetBoolean(3) ? "bloqueado" : "activo";
            string clave = r.GetBoolean(2) ? "temporal" : "propia";
            string acceso = r.IsDBNull(4) ? "-" : (r.GetDateTime(4) + Ecuador).ToString("yyyy-MM-dd HH:mm");
            Console.WriteLine($"{r.GetString(0),-30} {estado,-10} {clave,-12} {acceso,-23}");
        }
        return 0;
    }

    private static void MostrarTemporal(string usuario, string temporal)
    {
        Console.WriteLine($"Usuario: {usuario}");
        Console.WriteLine($"Contraseña temporal: {temporal}");
        Console.WriteLine("Entréguela en persona. Deberá cambiarla en su primer inicio de sesión.");
    }

    private static int NoExiste(string usuario)
    {
        Console.Error.WriteLine($"No existe el terapeuta {usuario}.");
        return 1;
    }

    private static int Ayuda()
    {
        Console.Error.WriteLine("""
            Comandos:
              terapeuta-nuevo <usuario>             Crea la cuenta con una contraseña temporal
              terapeuta-restablecer <usuario>       Nueva contraseña temporal y desbloqueo
              terapeuta-cerrar-sesiones <usuario>   Cierra sus sesiones en todos los equipos
              terapeuta-desactivar <usuario>        Bloquea la cuenta y cierra sus sesiones
              terapeuta-activar <usuario>           Reactiva la cuenta
              terapeuta-lista                       Lista las cuentas
            """);
        return 2;
    }
}
