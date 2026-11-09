// Comandos de asignación de pacientes a visores y de sesiones sin asignar
// (solo para desarrollo; en la fase 4 lo hará el portal).
//   docker compose exec api dotnet Rehab.Api.dll asignacion-nueva 7 P-001
//   docker compose exec api dotnet Rehab.Api.dll sesion-asignar <sesionId> P-001
using System.Text.RegularExpressions;
using MySqlConnector;

namespace Rehab.Api.Admin;

public static partial class ComandosAsignacion
{
    private const int HorasPorDefecto = 4;
    private static readonly TimeSpan HoraEcuador = TimeSpan.FromHours(-5);   // Ecuador continental, sin horario de verano

    [GeneratedRegex(@"^P-[0-9]{3,6}$")]
    private static partial Regex CodigoValido();

    public static async Task<int> EjecutarAsync(string[] args, MySqlDataSource ds)
    {
        ushort visorId = 0;
        bool tieneVisor = args.Length >= 2 && ushort.TryParse(args[1], out visorId) && visorId > 0;

        try
        {
            return args[0] switch
            {
                "asignacion-nueva" when tieneVisor && args.Length is 3 or 4 =>
                    await NuevaAsync(ds, visorId, args[2], args.Length == 4 ? args[3] : null),
                "asignacion-quitar" when tieneVisor && args.Length == 2 => await QuitarAsync(ds, visorId),
                "asignacion-lista" when args.Length == 1 => await ListaAsync(ds),
                "sesion-sin-asignar" when args.Length == 1 => await SinAsignarAsync(ds),
                "sesion-asignar" when args.Length == 3 => await AsignarSesionAsync(ds, args[1], args[2]),
                _ => Ayuda(),
            };
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"Error de base de datos ({ex.Number}).");
            return 1;
        }
    }

    // Asigna un paciente a un visor. Reemplaza la asignación anterior del visor.
    private static async Task<int> NuevaAsync(MySqlDataSource ds, ushort visorId, string codigo, string? horasTexto)
    {
        int horas = HorasPorDefecto;
        if (horasTexto is not null && (!int.TryParse(horasTexto, out horas) || horas is < 1 or > 12))
        {
            Console.Error.WriteLine("Horas no válidas: entre 1 y 12.");
            return 1;
        }

        await using var cn = await ds.OpenConnectionAsync();

        if (!await VisorActivoAsync(cn, visorId))
        {
            Console.Error.WriteLine($"El visor #{visorId} no existe o no está activo.");
            return 1;
        }

        var paciente = await BuscarPacienteAsync(cn, codigo);
        if (paciente is null) return 1;

        await using var cmd = new MySqlCommand(
            "INSERT INTO asignaciones (visor_id, paciente_id, expira_utc) " +
            "VALUES (@visor, @paciente, UTC_TIMESTAMP(3) + INTERVAL @horas HOUR) " +
            "ON DUPLICATE KEY UPDATE paciente_id = VALUES(paciente_id), creada_utc = UTC_TIMESTAMP(3), " +
            "expira_utc = VALUES(expira_utc)", cn);
        cmd.Parameters.AddWithValue("@visor", visorId);
        cmd.Parameters.AddWithValue("@paciente", paciente.Value.Id);
        cmd.Parameters.AddWithValue("@horas", horas);
        await cmd.ExecuteNonQueryAsync();

        var hasta = DateTime.UtcNow.AddHours(horas) + HoraEcuador;
        Console.WriteLine($"Visor #{visorId} asignado a {codigo.ToUpperInvariant()} hasta las {hasta:HH:mm} (hora de Ecuador).");
        return 0;
    }

    private static async Task<int> QuitarAsync(MySqlDataSource ds, ushort visorId)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("DELETE FROM asignaciones WHERE visor_id = @visor", cn);
        cmd.Parameters.AddWithValue("@visor", visorId);
        int filas = await cmd.ExecuteNonQueryAsync();

        Console.WriteLine(filas > 0 ? $"Asignación del visor #{visorId} quitada." : $"El visor #{visorId} no tenía asignación.");
        return 0;
    }

    private static async Task<int> ListaAsync(MySqlDataSource ds)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT a.visor_id, v.nombre, p.codigo, a.expira_utc, a.expira_utc > UTC_TIMESTAMP(3) " +
            "FROM asignaciones a JOIN visores v ON v.id = a.visor_id JOIN pacientes p ON p.id = a.paciente_id " +
            "ORDER BY a.visor_id", cn);
        await using var r = await cmd.ExecuteReaderAsync();

        Console.WriteLine($"{"#",-4} {"Visor",-30} {"Paciente",-10} {"Vence (Ecuador)",-16} {"Estado",-8}");
        while (await r.ReadAsync())
        {
            var vence = r.GetDateTime(3) + HoraEcuador;
            string estado = Convert.ToInt32(r.GetValue(4)) == 1 ? "vigente" : "vencida";
            Console.WriteLine($"{r.GetUInt16(0),-4} {r.GetString(1),-30} {r.GetString(2),-10} {vence,-16:yyyy-MM-dd HH:mm} {estado,-8}");
        }
        return 0;
    }

    // Sesiones recibidas sin paciente (el terapeuta no asignó antes o no había internet)
    private static async Task<int> SinAsignarAsync(MySqlDataSource ds)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT CAST(s.sesion_uuid AS CHAR), s.visor_id, s.inicio_utc, s.estado, c.distancia_m " +
            "FROM sesiones s LEFT JOIN caminata_resultados c ON c.sesion_id = s.id " +
            "WHERE s.paciente_id IS NULL ORDER BY s.inicio_utc DESC LIMIT 50", cn);
        await using var r = await cmd.ExecuteReaderAsync();

        Console.WriteLine($"{"sesionId",-36} {"Visor",-6} {"Inicio (Ecuador)",-17} {"Estado",-11} {"Metros",7}");
        while (await r.ReadAsync())
        {
            var inicio = r.GetDateTime(2) + HoraEcuador;
            string metros = r.IsDBNull(4) ? "-" : Convert.ToDecimal(r.GetValue(4)).ToString("F1");
            Console.WriteLine($"{r.GetString(0),-36} {r.GetUInt16(1),-6} {inicio,-17:yyyy-MM-dd HH:mm} {r.GetString(3),-11} {metros,7}");
        }
        return 0;
    }

    // Asigna un paciente a una sesión que llegó sin paciente
    private static async Task<int> AsignarSesionAsync(MySqlDataSource ds, string sesionTexto, string codigo)
    {
        if (!Guid.TryParse(sesionTexto, out var sesionId))
        {
            Console.Error.WriteLine("sesionId no válido.");
            return 1;
        }

        await using var cn = await ds.OpenConnectionAsync();
        var paciente = await BuscarPacienteAsync(cn, codigo);
        if (paciente is null) return 1;

        await using var cmd = new MySqlCommand(
            "UPDATE sesiones SET paciente_id = @paciente, asignada_utc = UTC_TIMESTAMP(3), " +
            "es_prueba = es_prueba OR @prueba WHERE sesion_uuid = @uuid AND paciente_id IS NULL", cn);
        cmd.Parameters.AddWithValue("@paciente", paciente.Value.Id);
        cmd.Parameters.AddWithValue("@prueba", paciente.Value.EsPrueba);
        cmd.Parameters.AddWithValue("@uuid", sesionId.ToString("D"));
        int filas = await cmd.ExecuteNonQueryAsync();

        if (filas == 0)
        {
            Console.Error.WriteLine("La sesión no existe o ya tiene paciente.");
            return 1;
        }
        Console.WriteLine($"Sesión asignada a {codigo.ToUpperInvariant()}.");
        return 0;
    }

    private static async Task<bool> VisorActivoAsync(MySqlConnection cn, ushort visorId)
    {
        await using var cmd = new MySqlCommand("SELECT 1 FROM visores WHERE id = @id AND estado = 'activo'", cn);
        cmd.Parameters.AddWithValue("@id", visorId);
        return await cmd.ExecuteScalarAsync() is not null;
    }

    // Paciente registrado: de prueba, o real con consentimiento
    private static async Task<(int Id, bool EsPrueba)?> BuscarPacienteAsync(MySqlConnection cn, string codigo)
    {
        codigo = codigo.Trim().ToUpperInvariant();
        if (!CodigoValido().IsMatch(codigo))
        {
            Console.Error.WriteLine("Código no válido. Formato: P- seguido de 3 a 6 dígitos.");
            return null;
        }

        await using var cmd = new MySqlCommand(
            "SELECT id, es_prueba FROM pacientes WHERE codigo = @codigo " +
            "AND (es_prueba OR consentimiento_utc IS NOT NULL)", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync())
        {
            Console.Error.WriteLine($"El paciente {codigo} no está registrado.");
            return null;
        }
        return (Convert.ToInt32(r.GetValue(0)), r.GetBoolean(1));
    }

    private static int Ayuda()
    {
        Console.Error.WriteLine("""
            Comandos:
              asignacion-nueva <visor> <paciente> [horas]   Asigna un paciente a un visor (por defecto 4 horas)
              asignacion-quitar <visor>                      Quita la asignación del visor
              asignacion-lista                               Muestra las asignaciones
              sesion-sin-asignar                             Sesiones recibidas sin paciente
              sesion-asignar <sesionId> <paciente>           Asigna un paciente a una sesión sin paciente
            """);
        return 2;
    }
}
