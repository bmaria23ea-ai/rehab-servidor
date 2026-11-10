using System.Globalization;
using System.Text.RegularExpressions;
using MySqlConnector;

namespace Rehab.Portal.Datos;

public sealed record PacienteFila(string Codigo, bool EsPrueba, DateTime? Consentimiento, string? Version, int Sesiones, DateTime? UltimaSesion);

public sealed partial class Pacientes(MySqlDataSource ds)
{
    [GeneratedRegex("^P-[0-9]{3,6}$")]
    private static partial Regex CodigoValido();

    [GeneratedRegex("^[A-Za-z0-9._-]{1,20}$")]
    private static partial Regex VersionValida();

    public async Task<List<PacienteFila>> ListarAsync()
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT p.codigo, p.es_prueba, p.consentimiento_utc, p.consentimiento_version, COUNT(s.id), MAX(s.inicio_utc) " +
            "FROM pacientes p LEFT JOIN sesiones s ON s.paciente_id = p.id " +
            "GROUP BY p.id ORDER BY p.es_prueba, p.codigo", cn);
        await using var r = await cmd.ExecuteReaderAsync();

        var lista = new List<PacienteFila>();
        while (await r.ReadAsync())
            lista.Add(new PacienteFila(r.GetString(0), r.GetBoolean(1),
                r.IsDBNull(2) ? null : r.GetDateTime(2), r.IsDBNull(3) ? null : r.GetString(3),
                Convert.ToInt32(r.GetValue(4)), r.IsDBNull(5) ? null : r.GetDateTime(5)));
        return lista;
    }

    public async Task<PacienteFila?> BuscarAsync(string codigo) =>
        (await ListarAsync()).FirstOrDefault(p => p.Codigo == codigo);

    // Derecho de supresión (LOPDP): borra al paciente; sus sesiones y resultados se borran en cascada.
    public async Task<bool> EliminarAsync(string codigo)
    {
        if (codigo == "P-000") return false;
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("DELETE FROM pacientes WHERE codigo = @codigo", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // Derecho de acceso y portabilidad (LOPDP): todas sus sesiones en CSV para Excel (separador ; y coma decimal).
    public async Task<string> ExportarCsvAsync(string codigo)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT s.inicio_utc, s.ejercicio, s.estado, s.motivo_fin, s.duracion_configurada_s, s.duracion_real_s, " +
            "c.distancia_m, c.num_paradas, c.tiempo_detenido_s, " +
            "(SELECT GROUP_CONCAT(CAST(m.distancia_m AS CHAR) ORDER BY m.minuto SEPARATOR ' | ') FROM caminata_minutos m WHERE m.sesion_id = s.id) " +
            "FROM sesiones s JOIN pacientes p ON p.id = s.paciente_id " +
            "LEFT JOIN caminata_resultados c ON c.sesion_id = s.id " +
            "WHERE p.codigo = @codigo ORDER BY s.inicio_utc", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        await using var r = await cmd.ExecuteReaderAsync();

        var csv = new System.Text.StringBuilder();
        csv.AppendLine("Paciente;Fecha (Ecuador);Ejercicio;Estado;Duración prevista (min);Duración real (min);Distancia (m);Paradas;Tiempo detenido (s);Metros por minuto");
        while (await r.ReadAsync())
        {
            string? Dec(int i) => r.IsDBNull(i) ? "" : Textos.Num(Convert.ToDecimal(r.GetValue(i)), "0.##");
            csv.Append(codigo).Append(';')
               .Append(Hora.FechaHora(r.GetDateTime(0))).Append(';')
               .Append(Textos.Ejercicio(r.GetString(1))).Append(';')
               .Append(Textos.Motivo(r.GetString(3))).Append(';')
               .Append(Textos.Num(Convert.ToDecimal(r.GetValue(4)) / 60m, "0.##")).Append(';')
               .Append(Textos.Num(Convert.ToDecimal(r.GetValue(5)) / 60m, "0.##")).Append(';')
               .Append(Dec(6)).Append(';')
               .Append(r.IsDBNull(7) ? "" : r.GetValue(7).ToString()).Append(';')
               .Append(Dec(8)).Append(';')
               .AppendLine(r.IsDBNull(9) ? "" : r.GetString(9).Replace('.', ','));
        }
        return csv.ToString();
    }

    // Devuelve un mensaje si algo no es válido; si todo está bien, registra y devuelve null.
    public async Task<string?> RegistrarAsync(string codigo, bool prueba, string fecha, string version)
    {
        codigo = codigo.Trim().ToUpperInvariant();
        version = version.Trim();

        if (!CodigoValido().IsMatch(codigo))
            return "Código no válido. Use P- y de 3 a 6 dígitos, por ejemplo P-001.";
        if (codigo == "P-000")
            return "P-000 está reservado para pruebas.";

        DateOnly? dia = null;
        if (!prueba)
        {
            if (!DateOnly.TryParseExact(fecha, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                || d.Year < 2020 || d > Hora.HoyEcuador())
                return "Indique la fecha del consentimiento (no puede ser futura).";
            if (!VersionValida().IsMatch(version))
                return "Indique la versión del formulario de consentimiento, por ejemplo v1.";
            dia = d;
        }

        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO pacientes (codigo, es_prueba, consentimiento_utc, consentimiento_version) " +
            "VALUES (@codigo, @prueba, @fecha, @version)", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@prueba", prueba);
        cmd.Parameters.AddWithValue("@fecha", dia is { } x ? x.ToDateTime(TimeOnly.MinValue) : DBNull.Value);
        cmd.Parameters.AddWithValue("@version", prueba ? DBNull.Value : version);
        try
        {
            await cmd.ExecuteNonQueryAsync();
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return $"Ya existe el paciente {codigo}.";
        }
        return null;
    }
}
