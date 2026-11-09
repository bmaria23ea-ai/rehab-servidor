// Comandos de administración de pacientes (solo para desarrollo; en la fase 4 lo hará el portal).
//   docker compose exec api dotnet Rehab.Api.dll paciente-nuevo P-001 2026-11-08 v1
//   docker compose exec api dotnet Rehab.Api.dll paciente-prueba P-900
//   docker compose exec api dotnet Rehab.Api.dll paciente-lista
using System.Globalization;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace Rehab.Api.Admin;

public static partial class ComandosPaciente
{
    [GeneratedRegex(@"^P-[0-9]{3,6}$")]
    private static partial Regex CodigoValido();

    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,20}$")]
    private static partial Regex VersionValida();

    public static async Task<int> EjecutarAsync(string[] args, MySqlDataSource ds)
    {
        try
        {
            return args[0] switch
            {
                "paciente-nuevo" when args.Length == 4 => await NuevoAsync(ds, args[1], args[2], args[3]),
                "paciente-prueba" when args.Length == 2 => await PruebaAsync(ds, args[1]),
                "paciente-lista" when args.Length == 1 => await ListaAsync(ds),
                _ => Ayuda(),
            };
        }
        catch (MySqlException ex) when (ex.Number == 1062)   // código duplicado
        {
            Console.Error.WriteLine("Ya existe un paciente con ese código.");
            return 1;
        }
        catch (MySqlException ex)
        {
            Console.Error.WriteLine($"Error de base de datos ({ex.Number}).");
            return 1;
        }
    }

    // Paciente real: exige la fecha y la versión del consentimiento firmado (LOPDP)
    private static async Task<int> NuevoAsync(MySqlDataSource ds, string codigo, string fecha, string version)
    {
        codigo = codigo.Trim().ToUpperInvariant();
        if (!CodigoValido().IsMatch(codigo) || codigo == "P-000")
        {
            Console.Error.WriteLine("Código no válido. Formato: P- seguido de 3 a 6 dígitos (P-000 está reservado para pruebas).");
            return 1;
        }
        if (!DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dia)
            || dia > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) || dia.Year < 2020)
        {
            Console.Error.WriteLine("Fecha de consentimiento no válida. Formato AAAA-MM-DD, no puede ser futura.");
            return 1;
        }
        if (!VersionValida().IsMatch(version))
        {
            Console.Error.WriteLine("Versión del formulario no válida (1 a 20 letras, números, punto o guion).");
            return 1;
        }

        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO pacientes (codigo, es_prueba, consentimiento_utc, consentimiento_version) " +
            "VALUES (@codigo, FALSE, @fecha, @version)", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@fecha", dia.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@version", version);
        await cmd.ExecuteNonQueryAsync();

        Console.WriteLine($"Paciente {codigo} registrado (consentimiento {fecha}, formulario {version}).");
        return 0;
    }

    // Paciente ficticio para pruebas: no requiere consentimiento
    private static async Task<int> PruebaAsync(MySqlDataSource ds, string codigo)
    {
        codigo = codigo.Trim().ToUpperInvariant();
        if (!CodigoValido().IsMatch(codigo))
        {
            Console.Error.WriteLine("Código no válido. Formato: P- seguido de 3 a 6 dígitos.");
            return 1;
        }

        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("INSERT INTO pacientes (codigo, es_prueba) VALUES (@codigo, TRUE)", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        await cmd.ExecuteNonQueryAsync();

        Console.WriteLine($"Paciente de prueba {codigo} registrado.");
        return 0;
    }

    private static async Task<int> ListaAsync(MySqlDataSource ds)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT codigo, es_prueba, consentimiento_utc, consentimiento_version FROM pacientes ORDER BY codigo", cn);
        await using var r = await cmd.ExecuteReaderAsync();

        Console.WriteLine($"{"Código",-10} {"Tipo",-8} {"Consentimiento",-15} {"Formulario",-12}");
        while (await r.ReadAsync())
        {
            string tipo = r.GetBoolean(1) ? "prueba" : "real";
            string fecha = r.IsDBNull(2) ? "-" : r.GetDateTime(2).ToString("yyyy-MM-dd");
            string version = r.IsDBNull(3) ? "-" : r.GetString(3);
            Console.WriteLine($"{r.GetString(0),-10} {tipo,-8} {fecha,-15} {version,-12}");
        }
        return 0;
    }

    private static int Ayuda()
    {
        Console.Error.WriteLine("""
            Comandos:
              paciente-nuevo <código> <AAAA-MM-DD> <versión>   Paciente real con su consentimiento firmado
              paciente-prueba <código>                         Paciente ficticio para pruebas
              paciente-lista                                   Muestra los pacientes
            """);
        return 2;
    }
}
