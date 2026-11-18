using System.Globalization;
using MySqlConnector;

namespace Rehab.Portal.Datos;

public sealed record SesionFila(ulong Id, string? Paciente, string Ejercicio, bool EsPrueba, DateTime Inicio,
                                decimal DuracionS, string Estado, string Motivo, decimal? DistanciaM,
                                int? Paradas, decimal? DetenidoS, string Visor, string Codigo, Gimnasio? Gym = null,
                                int? RetirosMarcha = null, Respiracion? Resp = null,
                                VidaCotidiana? Vida = null)
{
    public bool Completa => Estado == "completa";
}

// Resumen de una sesión del gimnasio; totales calculados al consultar
public sealed record Gimnasio(string Escena, int Series, int MetaSerie, decimal Umbral, int Retiros,
                              int Reps, int Validas, int SeriesHechas)
{
    public int Meta => Series * MetaSerie;
    public int Porcentaje => Meta > 0 ? (int)Math.Round(100m * Reps / Meta, MidpointRounding.AwayFromZero) : 0;
    public bool Lograda(int reps) => reps >= MetaSerie * Umbral;
}

public sealed record Serie(int Reps, int Validas);

// Resumen de una sesión de respiración; serie completa = todas sus repeticiones
public sealed record Respiracion(string Escena, int Series, int RepsSerie, int Retiros, int Reps, int SeriesHechas,
                                 int SeriesCompletas)
{
    public int Meta => Series * RepsSerie;
    public bool Completa(int reps) => reps >= RepsSerie;
}

// Resumen de una sesión de vida cotidiana; Actividades = logros distintos del catálogo
public sealed record VidaCotidiana(string Escena, decimal SegundosActivos, int Actividades, int Reps)
{
    public static int Total => Textos.Actividades.Length;
}

public sealed record Logro(string Codigo, int Cantidad, decimal PrimerSegundo);

public sealed record ResumenHoy(int Sesiones, int Interrumpidas, int SinPaciente);

public sealed record SesionDetalle(SesionFila Sesion, decimal? TiempoDetenidoS, int? Paradas, decimal? LargoPistaM,
                                   decimal? DuracionConfiguradaS, List<decimal> Minutos);

public sealed class Sesiones(MySqlDataSource ds)
{
    public const string SinPaciente = "sin";
    public const string Marcha = "caminata_6m";
    public const string Gimnasio = "gimnasio";
    public const string Respiracion = "respiracion";
    public const string Vida = "vida_cotidiana";

    public static readonly string[] EjerciciosRespiracion = ["apnea_sedestacion"];

    // Orden de los ejercicios del gimnasio en el portal
    public static readonly string[] EjerciciosGimnasio =
        ["brazos_frontal", "hombros", "giro_torso", "sentadilla_libre", "sentadilla_pesa"];

    private static readonly string FiltroGimnasio =
        "s.ejercicio IN (" + string.Join(", ", EjerciciosGimnasio.Select(e => $"'{e}'")) + ") ";
    private static readonly string FiltroRespiracion =
        "s.ejercicio IN (" + string.Join(", ", EjerciciosRespiracion.Select(e => $"'{e}'")) + ") ";

    private const string Base =
        "SELECT s.id, p.codigo, s.ejercicio, s.es_prueba, s.inicio_utc, s.duracion_real_s, s.estado, s.motivo_fin, c.distancia_m, " +
        "c.tiempo_detenido_s, c.num_paradas, c.largo_pista_m, s.duracion_configurada_s, v.nombre, s.sesion_uuid, s.visor_id, " +
        "s.escena, g.series_config, g.meta_por_serie, g.umbral_logro, g.retiros_visor, " +
        "(SELECT SUM(x.reps) FROM gimnasio_series x WHERE x.sesion_id = s.id), " +
        "(SELECT SUM(x.reps_validas) FROM gimnasio_series x WHERE x.sesion_id = s.id), " +
        "(SELECT COUNT(*) FROM gimnasio_series x WHERE x.sesion_id = s.id), c.retiros_visor, " +
        "rs.series_config, rs.reps_por_serie, rs.retiros_visor, " +
        "(SELECT SUM(y.reps) FROM respiracion_series y WHERE y.sesion_id = s.id), " +
        "(SELECT COUNT(*) FROM respiracion_series y WHERE y.sesion_id = s.id), " +
        "(SELECT COUNT(*) FROM respiracion_series y WHERE y.sesion_id = s.id AND y.reps >= rs.reps_por_serie), " +
        "vc.segundos_activos, " +
        "(SELECT COUNT(*) FROM vida_cotidiana_logros z WHERE z.sesion_id = s.id), " +
        "(SELECT SUM(z.cantidad) FROM vida_cotidiana_logros z WHERE z.sesion_id = s.id) " +
        "FROM sesiones s LEFT JOIN pacientes p ON p.id = s.paciente_id " +
        "JOIN visores v ON v.id = s.visor_id " +
        "LEFT JOIN caminata_resultados c ON c.sesion_id = s.id " +
        "LEFT JOIN gimnasio_sesiones g ON g.sesion_id = s.id " +
        "LEFT JOIN respiracion_sesiones rs ON rs.sesion_id = s.id " +
        "LEFT JOIN vida_cotidiana_sesiones vc ON vc.sesion_id = s.id ";

    // ejercicio: null = todos, Marcha, Gimnasio, Respiracion o Vida
    public async Task<List<SesionFila>> ListarAsync(string? paciente, string? ejercicio = null)
    {
        var condiciones = new List<string>();
        if (paciente == SinPaciente) condiciones.Add("s.paciente_id IS NULL ");
        else if (!string.IsNullOrEmpty(paciente)) condiciones.Add("p.codigo = @paciente ");
        if (ejercicio == Marcha) condiciones.Add("s.ejercicio = 'caminata_6m' ");
        else if (ejercicio == Gimnasio) condiciones.Add(FiltroGimnasio);
        else if (ejercicio == Respiracion) condiciones.Add(FiltroRespiracion);
        else if (ejercicio == Vida) condiciones.Add("s.ejercicio = 'vida_cotidiana' ");
        string filtro = condiciones.Count == 0 ? "" : "WHERE " + string.Join("AND ", condiciones);

        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(Base + filtro + "ORDER BY s.inicio_utc DESC LIMIT 200", cn);
        cmd.Parameters.AddWithValue("@paciente", paciente ?? "");
        return await LeerAsync(cmd);
    }

    public async Task<ResumenHoy> ResumenHoyAsync()
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT COALESCE(SUM(inicio_utc >= @hoy), 0), COALESCE(SUM(inicio_utc >= @hoy AND estado <> 'completa'), 0), " +
            "COALESCE(SUM(paciente_id IS NULL), 0) FROM sesiones", cn);
        cmd.Parameters.AddWithValue("@hoy", Hora.InicioHoyUtc());
        await using var r = await cmd.ExecuteReaderAsync();
        await r.ReadAsync();
        return new ResumenHoy(Convert.ToInt32(r.GetValue(0)), Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2)));
    }

    // Interrumpidas de hoy y todas las que no tienen paciente
    public async Task<List<SesionFila>> PendientesAsync()
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(Base +
            "WHERE s.paciente_id IS NULL OR (s.inicio_utc >= @hoy AND s.estado <> 'completa') " +
            "ORDER BY s.inicio_utc DESC LIMIT 20", cn);
        cmd.Parameters.AddWithValue("@hoy", Hora.InicioHoyUtc());
        return await LeerAsync(cmd);
    }

    public async Task<List<SesionFila>> RecientesAsync(int cuantas)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(Base + "ORDER BY s.inicio_utc DESC LIMIT @n", cn);
        cmd.Parameters.AddWithValue("@n", cuantas);
        return await LeerAsync(cmd);
    }

    private static async Task<List<SesionFila>> LeerAsync(MySqlCommand cmd)
    {
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
                                  Dec(r, 11), Dec(r, 12), []);
        }

        await using var min = new MySqlCommand(
            "SELECT distancia_m FROM caminata_minutos WHERE sesion_id = @id ORDER BY minuto", cn);
        min.Parameters.AddWithValue("@id", id);
        await using var m = await min.ExecuteReaderAsync();
        while (await m.ReadAsync()) d.Minutos.Add(Convert.ToDecimal(m.GetValue(0)));
        return d;
    }

    // Series del gimnasio de varias sesiones, en orden
    public async Task<Dictionary<ulong, List<Serie>>> SeriesAsync(IReadOnlyCollection<ulong> ids)
    {
        var d = new Dictionary<ulong, List<Serie>>();
        if (ids.Count == 0) return d;
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        var nombres = new List<string>(ids.Count);
        int i = 0;
        foreach (var id in ids)
        {
            nombres.Add($"@i{i}");
            cmd.Parameters.AddWithValue($"@i{i++}", id);
        }
        cmd.CommandText = "SELECT sesion_id, reps, reps_validas FROM gimnasio_series " +
                          $"WHERE sesion_id IN ({string.Join(", ", nombres)}) ORDER BY sesion_id, serie";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            ulong id = Convert.ToUInt64(r.GetValue(0));
            if (!d.TryGetValue(id, out var lista)) d[id] = lista = [];
            lista.Add(new Serie(Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2))));
        }
        return d;
    }

    // Repeticiones por serie de varias sesiones de respiración, en orden
    public async Task<Dictionary<ulong, List<int>>> SeriesRespiracionAsync(IReadOnlyCollection<ulong> ids)
    {
        var d = new Dictionary<ulong, List<int>>();
        if (ids.Count == 0) return d;
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        var nombres = new List<string>(ids.Count);
        int i = 0;
        foreach (var id in ids)
        {
            nombres.Add($"@i{i}");
            cmd.Parameters.AddWithValue($"@i{i++}", id);
        }
        cmd.CommandText = "SELECT sesion_id, reps FROM respiracion_series " +
                          $"WHERE sesion_id IN ({string.Join(", ", nombres)}) ORDER BY sesion_id, serie";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            ulong id = Convert.ToUInt64(r.GetValue(0));
            if (!d.TryGetValue(id, out var lista)) d[id] = lista = [];
            lista.Add(Convert.ToInt32(r.GetValue(1)));
        }
        return d;
    }

    // Logros de varias sesiones de vida cotidiana, en el orden en que se hicieron por primera vez
    public async Task<Dictionary<ulong, List<Logro>>> LogrosAsync(IReadOnlyCollection<ulong> ids)
    {
        var d = new Dictionary<ulong, List<Logro>>();
        if (ids.Count == 0) return d;
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand { Connection = cn };
        var nombres = new List<string>(ids.Count);
        int i = 0;
        foreach (var id in ids)
        {
            nombres.Add($"@i{i}");
            cmd.Parameters.AddWithValue($"@i{i++}", id);
        }
        cmd.CommandText = "SELECT sesion_id, codigo, cantidad, primer_segundo FROM vida_cotidiana_logros " +
                          $"WHERE sesion_id IN ({string.Join(", ", nombres)}) ORDER BY sesion_id, primer_segundo, codigo";
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            ulong id = Convert.ToUInt64(r.GetValue(0));
            if (!d.TryGetValue(id, out var lista)) d[id] = lista = [];
            lista.Add(new Logro(r.GetString(1), Convert.ToInt32(r.GetValue(2)), Convert.ToDecimal(r.GetValue(3))));
        }
        return d;
    }

    // Última sesión recibida de cada visor
    public async Task<Dictionary<string, SesionFila>> UltimasPorVisorAsync()
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(Base +
            "WHERE s.id IN (SELECT MAX(id) FROM sesiones GROUP BY visor_id)", cn);
        await using var r = await cmd.ExecuteReaderAsync();
        var d = new Dictionary<string, SesionFila>();
        while (await r.ReadAsync()) d[Convert.ToString(r.GetValue(15))!] = Fila(r);
        return d;
    }

    // null = dejarla sin paciente. El paciente de prueba marca la sesión como prueba.
    public async Task<bool> CambiarPacienteAsync(ulong id, string? codigo)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(codigo is null
            ? "UPDATE sesiones SET paciente_id = NULL, asignada_utc = NULL WHERE id = @id"
            : "UPDATE sesiones s JOIN pacientes p ON p.codigo = @codigo " +
              "SET s.paciente_id = p.id, s.asignada_utc = UTC_TIMESTAMP(3), s.es_prueba = s.es_prueba OR p.es_prueba " +
              "WHERE s.id = @id", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // Borra la sesión; resultados, minutos, observación y mediciones se borran en cascada.
    public async Task<bool> EliminarAsync(ulong id)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("DELETE FROM sesiones WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }

    // Para el registro de actividad: "Sesión 3F9A"
    public async Task<string> NombreAsync(ulong id)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("SELECT sesion_uuid FROM sesiones WHERE id = @id", cn);
        cmd.Parameters.AddWithValue("@id", id);
        return await cmd.ExecuteScalarAsync() is { } uuid and not DBNull ? $"Sesión {CodigoCorto(uuid)}" : $"Sesión {id}";
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
        r.GetDateTime(4), Convert.ToDecimal(r.GetValue(5)), r.GetString(6), r.GetString(7), Dec(r, 8),
        r.IsDBNull(10) ? null : Convert.ToInt32(r.GetValue(10)), Dec(r, 9), r.GetString(13), CodigoCorto(r.GetValue(14)),
        r.IsDBNull(17) ? null : new Gimnasio(Textos.Escena(r.GetString(16)), Convert.ToInt32(r.GetValue(17)),
            Convert.ToInt32(r.GetValue(18)), Convert.ToDecimal(r.GetValue(19)), Convert.ToInt32(r.GetValue(20)),
            r.IsDBNull(21) ? 0 : Convert.ToInt32(r.GetValue(21)), r.IsDBNull(22) ? 0 : Convert.ToInt32(r.GetValue(22)),
            Convert.ToInt32(r.GetValue(23))),
        r.IsDBNull(24) ? null : Convert.ToInt32(r.GetValue(24)),
        r.IsDBNull(25) ? null : new Respiracion(Textos.Escena(r.GetString(16)), Convert.ToInt32(r.GetValue(25)),
            Convert.ToInt32(r.GetValue(26)), Convert.ToInt32(r.GetValue(27)),
            r.IsDBNull(28) ? 0 : Convert.ToInt32(r.GetValue(28)), Convert.ToInt32(r.GetValue(29)),
            Convert.ToInt32(r.GetValue(30))),
        r.IsDBNull(31) ? null : new VidaCotidiana(Textos.Escena(r.GetString(16)), Convert.ToDecimal(r.GetValue(31)),
            Convert.ToInt32(r.GetValue(32)), r.IsDBNull(33) ? 0 : Convert.ToInt32(r.GetValue(33))));

    // Primeros 4 caracteres del UUID de la sesión; el visor muestra el mismo código
    public static string CodigoCorto(object uuid) =>
        (uuid?.ToString() ?? "").Replace("-", "")[..4].ToUpperInvariant();

    private static decimal? Dec(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToDecimal(r.GetValue(i));
}

// Textos para el terapeuta.
public static class Textos
{
    public static string Ejercicio(string e) => e switch
    {
        "caminata_6m" => "Marcha",
        "brazos_frontal" => "Elevación frontal",
        "hombros" => "Press de hombro",
        "giro_torso" => "Giro de torso",
        "sentadilla_libre" => "Sentadilla libre",
        "sentadilla_pesa" => "Sentadilla con barra",
        "apnea_sedestacion" => "Apnea en sedestación",
        "vida_cotidiana" => "Vida cotidiana",
        _ => e,
    };

    // Catálogo de vida cotidiana, en el orden del portal y del Excel
    public static readonly (string Codigo, string Texto)[] Actividades =
    [
        ("libro_estante_medio", "Colocó un libro en el estante medio"),
        ("libro_estante_alto", "Colocó un libro en el estante alto"),
        ("libro_recogido_piso", "Recogió un libro del piso"),
        ("pesa_levantada", "Levantó una pesa del piso"),
        ("gato_acariciado", "Acarició al gato"),
        ("puerta_deslizada", "Deslizó la puerta"),
        ("ventana_deslizada", "Deslizó la ventana"),
        ("pelota_lanzada", "Lanzó la pelota"),
        ("bocado_comido", "Comió un bocado con el cubierto"),
    ];

    public static string Actividad(string codigo) =>
        Array.Find(Actividades, a => a.Codigo == codigo).Texto ?? codigo;

    // "F-Hombros_gym" -> "F"
    public static string Escena(string escena) =>
        escena.Length > 1 && escena[1] == '-' && escena[0] is 'F' or 'M' ? escena[..1] : "-";

    // Dato principal de la sesión en las listas: metros, repeticiones o actividades
    public static string Logro(SesionFila s) =>
        s.Gym is { } g ? $"{g.Reps} / {g.Meta} rep." :
        s.Resp is { } r ? $"{r.Reps} / {r.Meta} rep." :
        s.Vida is { } v ? $"{v.Actividades} / {VidaCotidiana.Total} actividades" : $"{Num(s.DistanciaM, "0.##")} m";

    public static string Motivo(string m) => m switch
    {
        "completada" => "Completa",
        "detenida" => "Finalizada antes",
        "salida_menu" => "Salió al menú",
        "visor_retirado" => "Retiró el visor",
        "app_pausada" => "Aplicación en pausa",
        _ => "Error",
    };

    public static string Paradas(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "-";

    // 15.4 -> "15 s"
    public static string Segundos(decimal? s) => s is null ? "-" : Num(s, "0") + " s";

    // 46.03 -> "46,0"
    public static string Num(decimal? n, string formato = "0.0") =>
        n is { } x ? x.ToString(formato, CultureInfo.InvariantCulture).Replace('.', ',') : "-";

    public static string Minutos(decimal segundos) => Num(segundos / 60m) + " min";

    // Gimnasio: 113.6 -> "1 min 54 s"; 45 -> "45 s"; 120 -> "2 min"
    public static string MinSeg(decimal segundos)
    {
        int total = (int)Math.Round(segundos, MidpointRounding.AwayFromZero);
        int min = total / 60, seg = total % 60;
        return min == 0 ? $"{seg} s" : seg == 0 ? $"{min} min" : $"{min} min {seg} s";
    }

    // Duración en las listas generales: minutos y segundos salvo la marcha, en minutos
    public static string Duracion(SesionFila s) =>
        s.Gym is null && s.Resp is null && s.Vida is null ? Minutos(s.DuracionS) : MinSeg(s.DuracionS);

    // Etiquetas cortas del gráfico de duración: 113 -> "1:53"
    public static string MinSegCorto(decimal segundos)
    {
        int total = (int)Math.Round(segundos, MidpointRounding.AwayFromZero);
        return $"{total / 60}:{total % 60:00}";
    }
}
