using System.Globalization;
using MySqlConnector;

namespace Rehab.Portal.Datos;

public sealed record SesionFila(ulong Id, string? Paciente, string Ejercicio, bool EsPrueba, DateTime Inicio,
                                decimal DuracionS, string Estado, string Motivo, decimal? DistanciaM);

public sealed record SesionDetalle(SesionFila Sesion, decimal? TiempoDetenidoS, int? Paradas, decimal? LargoPistaM,
                                   decimal DuracionConfiguradaS, List<decimal> Minutos);

public sealed class Sesiones(MySqlDataSource ds)
{
    public const string SinPaciente = "sin";

    private const string Base =
        "SELECT s.id, p.codigo, s.ejercicio, s.es_prueba, s.inicio_utc, s.duracion_real_s, s.estado, s.motivo_fin, c.distancia_m, " +
        "c.tiempo_detenido_s, c.num_paradas, c.largo_pista_m, s.duracion_configurada_s " +
        "FROM sesiones s LEFT JOIN pacientes p ON p.id = s.paciente_id " +
        "LEFT JOIN caminata_resultados c ON c.sesion_id = s.id ";

    public async Task<List<SesionFila>> ListarAsync(string? paciente)
    {
        string filtro = paciente switch
        {
            null or "" => "",
            SinPaciente => "WHERE s.paciente_id IS NULL ",
            _ => "WHERE p.codigo = @paciente ",
        };

        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(Base + filtro + "ORDER BY s.inicio_utc DESC LIMIT 200", cn);
        cmd.Parameters.AddWithValue("@paciente", paciente ?? "");
        await using var r = await cmd.ExecuteReaderAsync();

        var lista = new List<SesionFila>();
        while (await r.ReadAsync()) lista.Add(Fila(r));
        return lista;
    }

    public async Task<SesionDetalle?> BuscarAsync(ulong id)
    {
        await using var cn = await ds.OpenConnectionAsync();
        SesionDetalle? d;
        await using (var cmd = new MySqlCommand(Base + "WHERE s.id = @id", cn))
        {
            cmd.Parameters.AddWithValue("@id", id);
            await using var r = await cmd.ExecuteReaderAsync();
            if (!await r.ReadAsync()) return null;
            d = new SesionDetalle(Fila(r), Dec(r, 9), r.IsDBNull(10) ? null : Convert.ToInt32(r.GetValue(10)),
                                  Dec(r, 11), Convert.ToDecimal(r.GetValue(12)), []);
        }

        await using var min = new MySqlCommand(
            "SELECT distancia_m FROM caminata_minutos WHERE sesion_id = @id ORDER BY minuto", cn);
        min.Parameters.AddWithValue("@id", id);
        await using var m = await min.ExecuteReaderAsync();
        while (await m.ReadAsync()) d.Minutos.Add(Convert.ToDecimal(m.GetValue(0)));
        return d;
    }

    // Solo sesiones que llegaron sin paciente; el paciente de prueba marca la sesión como prueba.
    public async Task<bool> AsignarAsync(ulong id, string codigo)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "UPDATE sesiones s JOIN pacientes p ON p.codigo = @codigo " +
            "SET s.paciente_id = p.id, s.asignada_utc = UTC_TIMESTAMP(3), s.es_prueba = s.es_prueba OR p.es_prueba " +
            "WHERE s.id = @id AND s.paciente_id IS NULL", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    private static SesionFila Fila(MySqlDataReader r) => new(
        Convert.ToUInt64(r.GetValue(0)), r.IsDBNull(1) ? null : r.GetString(1), r.GetString(2), r.GetBoolean(3),
        r.GetDateTime(4), Convert.ToDecimal(r.GetValue(5)), r.GetString(6), r.GetString(7), Dec(r, 8));

    private static decimal? Dec(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
}

// Textos para el terapeuta.
public static class Textos
{
    public static string Ejercicio(string e) => e switch
    {
        "caminata_6m" => "Caminata",
        _ => e,
    };

    public static string Motivo(string m) => m switch
    {
        "completada" => "Completa",
        "detenida" => "Detenida antes de tiempo",
        "salida_menu" => "Salió al menú",
        "visor_retirado" => "Se quitó las gafas",
        "app_pausada" => "Aplicación en pausa",
        _ => "Error",
    };

    // 46.03 -> "46,0"
    public static string Num(decimal? n, string formato = "0.0") =>
        n is { } x ? x.ToString(formato, CultureInfo.InvariantCulture).Replace('.', ',') : "-";

    public static string Minutos(decimal segundos) => Num(segundos / 60m) + " min";
}
