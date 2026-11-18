using MySqlConnector;

namespace Rehab.Portal.Datos;

// Derecho de acceso y portabilidad (LOPDP): datos en Excel, un valor por casilla
public sealed class Exportacion(MySqlDataSource ds)
{
    private const int MinutosPrueba = 6;

    private const string Desde = "FROM sesiones s LEFT JOIN pacientes p ON p.id = s.paciente_id ";
    private const string Donde = "WHERE s.ejercicio = 'caminata_6m' AND (@codigo IS NULL OR p.codigo = @codigo) AND (@sesion IS NULL OR s.id = @sesion) ";

    // codigo null = todos, incluidas las sesiones sin paciente; sesion = solo esa sesión
    public async Task<byte[]> CrearAsync(string? codigo, ulong? sesion = null)
    {
        await using var cn = await ds.OpenConnectionAsync();
        var minutos = await MinutosAsync(cn, codigo, sesion);
        var mediciones = await MedicionesAsync(cn, codigo, sesion);
        int columnas = Math.Max(MinutosPrueba, minutos.Values.Select(m => m.Count).DefaultIfEmpty(0).Max());

        await using var cmd = new MySqlCommand(
            "SELECT s.id, p.codigo, s.sesion_uuid, s.inicio_utc, v.nombre, s.motivo_fin, s.duracion_real_s, " +
            "c.distancia_m, c.num_paradas, o.texto, c.tiempo_detenido_s, c.retiros_visor " + Desde +
            "JOIN visores v ON v.id = s.visor_id " +
            "LEFT JOIN caminata_resultados c ON c.sesion_id = s.id " +
            "LEFT JOIN sesion_observaciones o ON o.sesion_id = s.id " + Donde +
            "ORDER BY p.codigo IS NULL, p.codigo, s.inicio_utc", cn);
        Filtro(cmd, codigo, sesion);
        await using var r = await cmd.ExecuteReaderAsync();

        var filas = new List<Celda[]>();
        while (await r.ReadAsync())
        {
            ulong id = Convert.ToUInt64(r.GetValue(0));
            DateTime local = Hora.Ecuador(r.GetDateTime(3));
            var fila = new List<Celda>
            {
                new(r.IsDBNull(1) ? "Sin paciente" : r.GetString(1)),
                new(Sesiones.CodigoCorto(r.GetValue(2))),
                new(local.Date, 2),
                new(local.TimeOfDay, 3),
                new(r.GetString(4)),
                new(Textos.Motivo(r.GetString(5))),
                new(Convert.ToDecimal(r.GetValue(6)) / 60m, 5),
                new(r.IsDBNull(7) ? null : (object)Convert.ToDecimal(r.GetValue(7)), 4),
                new(r.IsDBNull(8) ? null : (object)Convert.ToInt32(r.GetValue(8))),
                new(r.IsDBNull(10) ? null : (object)Convert.ToDecimal(r.GetValue(10)), 5),
                new(r.IsDBNull(11) ? null : (object)Convert.ToInt32(r.GetValue(11))),
            };
            var m = minutos.TryGetValue(id, out var lista) ? lista : [];
            for (int i = 0; i < columnas; i++) fila.Add(new(i < m.Count ? (object)m[i] : null, 4));
            var med = mediciones.GetValueOrDefault(id);
            foreach (var mo in Mediciones.Cronologico)
            {
                var v = med?.GetValueOrDefault(mo.Clave);
                fila.Add(new(v?.Fc));
                fila.Add(new(v?.SpO2));
                fila.Add(new(v?.Borg, 5));
                if (mo.ConPa) { fila.Add(new(v?.PaSis)); fila.Add(new(v?.PaDia)); }
            }
            fila.Add(new(r.IsDBNull(9) ? null : r.GetString(9), 6));
            filas.Add(fila.ToArray());
        }

        var titulos = new List<string> { "Código", "Sesión", "Fecha", "Hora", "Visor", "Cómo terminó",
                                         "Tiempo de exposición (min)", "Distancia (m)", "Veces que se detuvo", "Tiempo detenido (s)",
                                         "Visor retirado" };
        var anchos = new List<double> { 13, 11, 12, 8, 14, 18, 14, 12, 12, 12, 12 };
        for (int i = 1; i <= columnas; i++) { titulos.Add($"Minuto {i} (m)"); anchos.Add(11); }
        foreach (var mo in Mediciones.Cronologico)
        {
            titulos.AddRange([$"{mo.Nombre} FC (lpm)", $"{mo.Nombre} SpO₂ (%)", $"{mo.Nombre} Borg disnea"]);
            if (mo.ConPa) titulos.AddRange([$"{mo.Nombre} PA sistólica (mmHg)", $"{mo.Nombre} PA diastólica (mmHg)"]);
        }
        while (anchos.Count < titulos.Count) anchos.Add(12);
        titulos.Add("Observaciones");
        anchos.Add(50);

        var marcha = new Hoja("Marcha", titulos.ToArray(), anchos.ToArray(), filas);
        await r.DisposeAsync();
        var gimnasio = await GimnasioAsync(cn, codigo, sesion);
        var respiracion = await RespiracionAsync(cn, codigo, sesion);
        var vida = await VidaAsync(cn, codigo, sesion);

        // Solo las hojas con datos; sin datos, la de la marcha con sus títulos
        var hojas = new List<Hoja>();
        if (marcha.Filas.Count > 0) hojas.Add(marcha);
        if (gimnasio.Filas.Count > 0) hojas.Add(gimnasio);
        if (respiracion.Filas.Count > 0) hojas.Add(respiracion);
        if (vida.Filas.Count > 0) hojas.Add(vida);
        if (hojas.Count == 0) hojas.Add(marcha);
        return Excel.Crear(hojas);
    }

    // Una fila por sesión de respiración; repeticiones de cada serie en columnas (vacías si no se llegó)
    private static async Task<Hoja> RespiracionAsync(MySqlConnection cn, string? codigo, ulong? sesion)
    {
        const string desde = "FROM sesiones s LEFT JOIN pacientes p ON p.id = s.paciente_id " +
                             "JOIN respiracion_sesiones rs ON rs.sesion_id = s.id ";
        var series = new Dictionary<ulong, List<int>>();
        await using (var cmd = new MySqlCommand(
            "SELECT y.sesion_id, y.reps " + desde + "JOIN respiracion_series y ON y.sesion_id = s.id " +
            DondeGimnasio + "ORDER BY y.sesion_id, y.serie", cn))
        {
            Filtro(cmd, codigo, sesion);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                ulong id = Convert.ToUInt64(r.GetValue(0));
                if (!series.TryGetValue(id, out var lista)) series[id] = lista = [];
                lista.Add(Convert.ToInt32(r.GetValue(1)));
            }
        }

        var datos = new List<object[]>();
        await using (var cmd = new MySqlCommand(
            "SELECT s.id, p.codigo, s.sesion_uuid, s.inicio_utc, v.nombre, s.ejercicio, s.escena, s.motivo_fin, " +
            "s.duracion_real_s, rs.series_config, rs.reps_por_serie, rs.retiros_visor, o.texto " + desde +
            "JOIN visores v ON v.id = s.visor_id " +
            "LEFT JOIN sesion_observaciones o ON o.sesion_id = s.id " + DondeGimnasio +
            "ORDER BY p.codigo IS NULL, p.codigo, s.inicio_utc", cn))
        {
            Filtro(cmd, codigo, sesion);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var fila = new object[13];
                r.GetValues(fila);
                datos.Add(fila);
            }
        }

        int columnas = datos.Select(f => Convert.ToInt32(f[9])).DefaultIfEmpty(2).Max();
        var filas = new List<Celda[]>();
        foreach (var f in datos)
        {
            var lista = series.GetValueOrDefault(Convert.ToUInt64(f[0]), []);
            int repsSerie = Convert.ToInt32(f[10]);
            DateTime local = Hora.Ecuador(Convert.ToDateTime(f[3]));
            var fila = new List<Celda>
            {
                new(f[1] is DBNull ? "Sin paciente" : (string)f[1]),
                new(Sesiones.CodigoCorto(f[2])),
                new(local.Date, 2),
                new(local.TimeOfDay, 3),
                new((string)f[4]),
                new(Textos.Ejercicio((string)f[5])),
                new(Textos.Escena((string)f[6])),
                new(Textos.Motivo((string)f[7])),
                new(TimeSpan.FromSeconds((double)Math.Round(Convert.ToDecimal(f[8]), MidpointRounding.AwayFromZero)), 7),
                new(lista.Sum()),
                new(Convert.ToInt32(f[9]) * repsSerie),
                new(lista.Count(x => x >= repsSerie)),
                new(Convert.ToInt32(f[11])),
            };
            for (int i = 0; i < columnas; i++) fila.Add(new(i < lista.Count ? lista[i] : null));
            fila.Add(new(f[12] is DBNull ? null : (string)f[12], 6));
            filas.Add(fila.ToArray());
        }

        var titulos = new List<string> { "Código", "Sesión", "Fecha", "Hora", "Visor", "Ejercicio", "Escena", "Cómo terminó",
                                         "Duración (min:s)", "Repeticiones", "Meta", "Series completas", "Visor retirado" };
        var anchos = new List<double> { 13, 11, 12, 8, 14, 22, 9, 18, 14, 13, 8, 12, 12 };
        for (int i = 1; i <= columnas; i++) { titulos.Add($"Serie {i} reps"); anchos.Add(12); }
        titulos.Add("Observaciones");
        anchos.Add(50);
        return new Hoja("Respiración", titulos.ToArray(), anchos.ToArray(), filas);
    }

    // Una fila por sesión de vida cotidiana; veces de cada actividad en columnas (0 si no la hizo)
    private static async Task<Hoja> VidaAsync(MySqlConnection cn, string? codigo, ulong? sesion)
    {
        const string desde = "FROM sesiones s LEFT JOIN pacientes p ON p.id = s.paciente_id " +
                             "JOIN vida_cotidiana_sesiones vc ON vc.sesion_id = s.id ";
        var logros = new Dictionary<ulong, Dictionary<string, int>>();
        await using (var cmd = new MySqlCommand(
            "SELECT z.sesion_id, z.codigo, z.cantidad " + desde + "JOIN vida_cotidiana_logros z ON z.sesion_id = s.id " +
            DondeGimnasio, cn))
        {
            Filtro(cmd, codigo, sesion);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                ulong id = Convert.ToUInt64(r.GetValue(0));
                if (!logros.TryGetValue(id, out var d)) logros[id] = d = [];
                d[r.GetString(1)] = Convert.ToInt32(r.GetValue(2));
            }
        }

        var filas = new List<Celda[]>();
        await using (var cmd = new MySqlCommand(
            "SELECT s.id, p.codigo, s.sesion_uuid, s.inicio_utc, v.nombre, s.motivo_fin, s.duracion_real_s, " +
            "vc.segundos_activos, o.texto " + desde +
            "JOIN visores v ON v.id = s.visor_id " +
            "LEFT JOIN sesion_observaciones o ON o.sesion_id = s.id " + DondeGimnasio +
            "ORDER BY p.codigo IS NULL, p.codigo, s.inicio_utc", cn))
        {
            Filtro(cmd, codigo, sesion);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var hechas = logros.GetValueOrDefault(Convert.ToUInt64(r.GetValue(0)), []);
                DateTime local = Hora.Ecuador(r.GetDateTime(3));
                var fila = new List<Celda>
                {
                    new(r.IsDBNull(1) ? "Sin paciente" : r.GetString(1)),
                    new(Sesiones.CodigoCorto(r.GetValue(2))),
                    new(local.Date, 2),
                    new(local.TimeOfDay, 3),
                    new(r.GetString(4)),
                    new(Textos.Motivo(r.GetString(5))),
                    new(MinSeg(Convert.ToDecimal(r.GetValue(6))), 7),
                    new(MinSeg(Convert.ToDecimal(r.GetValue(7))), 7),
                    new(hechas.Count),
                };
                foreach (var a in Textos.Actividades) fila.Add(new(hechas.GetValueOrDefault(a.Codigo)));
                fila.Add(new(r.IsDBNull(8) ? null : r.GetString(8), 6));
                filas.Add(fila.ToArray());
            }
        }

        var titulos = new List<string> { "Código", "Sesión", "Fecha", "Hora", "Visor", "Cómo terminó",
                                         "Duración (min:s)", "Tiempo activo aprox. (min:s)", "Actividades realizadas" };
        var anchos = new List<double> { 13, 11, 12, 8, 14, 18, 14, 16, 13 };
        foreach (var a in Textos.Actividades) { titulos.Add(a.Texto); anchos.Add(16); }
        titulos.Add("Observaciones");
        anchos.Add(50);
        return new Hoja("Vida cotidiana", titulos.ToArray(), anchos.ToArray(), filas);
    }

    // Duración redondeada al segundo, con formato [m]:ss
    private static TimeSpan MinSeg(decimal segundos) =>
        TimeSpan.FromSeconds((double)Math.Round(segundos, MidpointRounding.AwayFromZero));

    private const string DesdeGimnasio =
        "FROM sesiones s LEFT JOIN pacientes p ON p.id = s.paciente_id " +
        "JOIN gimnasio_sesiones g ON g.sesion_id = s.id ";
    private const string DondeGimnasio =
        "WHERE (@codigo IS NULL OR p.codigo = @codigo) AND (@sesion IS NULL OR s.id = @sesion) ";

    // Una fila por sesión del gimnasio; series en columnas (vacías si no se llegó a esa serie)
    private static async Task<Hoja> GimnasioAsync(MySqlConnection cn, string? codigo, ulong? sesion)
    {
        var series = new Dictionary<ulong, List<Serie>>();
        await using (var cmd = new MySqlCommand(
            "SELECT x.sesion_id, x.reps, x.reps_validas " + DesdeGimnasio +
            "JOIN gimnasio_series x ON x.sesion_id = s.id " + DondeGimnasio + "ORDER BY x.sesion_id, x.serie", cn))
        {
            Filtro(cmd, codigo, sesion);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                ulong id = Convert.ToUInt64(r.GetValue(0));
                if (!series.TryGetValue(id, out var lista)) series[id] = lista = [];
                lista.Add(new Serie(Convert.ToInt32(r.GetValue(1)), Convert.ToInt32(r.GetValue(2))));
            }
        }

        var datos = new List<(object[] Fila, int Series)>();
        await using (var cmd = new MySqlCommand(
            "SELECT s.id, p.codigo, s.sesion_uuid, s.inicio_utc, v.nombre, s.ejercicio, s.escena, s.motivo_fin, " +
            "s.duracion_real_s, g.series_config, g.meta_por_serie, g.retiros_visor, o.texto " + DesdeGimnasio +
            "JOIN visores v ON v.id = s.visor_id " +
            "LEFT JOIN sesion_observaciones o ON o.sesion_id = s.id " + DondeGimnasio +
            "ORDER BY p.codigo IS NULL, p.codigo, s.inicio_utc", cn))
        {
            Filtro(cmd, codigo, sesion);
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                var fila = new object[13];
                r.GetValues(fila);
                datos.Add((fila, Convert.ToInt32(fila[9])));
            }
        }

        int columnas = datos.Select(d => d.Series).DefaultIfEmpty(3).Max();
        var filas = new List<Celda[]>();
        foreach (var (f, _) in datos)
        {
            ulong id = Convert.ToUInt64(f[0]);
            var lista = series.GetValueOrDefault(id, []);
            int meta = Convert.ToInt32(f[9]) * Convert.ToInt32(f[10]);
            int reps = lista.Sum(x => x.Reps), validas = lista.Sum(x => x.Validas);
            DateTime local = Hora.Ecuador(Convert.ToDateTime(f[3]));
            var fila = new List<Celda>
            {
                new(f[1] is DBNull ? "Sin paciente" : (string)f[1]),
                new(Sesiones.CodigoCorto(f[2])),
                new(local.Date, 2),
                new(local.TimeOfDay, 3),
                new((string)f[4]),
                new(Textos.Ejercicio((string)f[5])),
                new(Textos.Escena((string)f[6])),
                new(Textos.Motivo((string)f[7])),
                new(TimeSpan.FromSeconds((double)Math.Round(Convert.ToDecimal(f[8]), MidpointRounding.AwayFromZero)), 7),
                new(reps),
                new(meta),
                new(meta > 0 ? (int)Math.Round(100m * reps / meta, MidpointRounding.AwayFromZero) : 0),
                new(validas),
                new(Convert.ToInt32(f[11])),
            };
            for (int i = 0; i < columnas; i++)
            {
                fila.Add(new(i < lista.Count ? lista[i].Reps : null));
                fila.Add(new(i < lista.Count ? lista[i].Validas : null));
            }
            fila.Add(new(f[12] is DBNull ? null : (string)f[12], 6));
            filas.Add(fila.ToArray());
        }

        var titulos = new List<string> { "Código", "Sesión", "Fecha", "Hora", "Visor", "Ejercicio", "Escena", "Cómo terminó",
                                         "Duración (min:s)", "Repeticiones", "Meta", "% de la meta", "Repeticiones válidas", "Visor retirado" };
        var anchos = new List<double> { 13, 11, 12, 8, 14, 20, 9, 18, 12, 13, 8, 12, 13, 12 };
        for (int i = 1; i <= columnas; i++)
        {
            titulos.AddRange([$"Serie {i} reps", $"Serie {i} válidas"]);
            anchos.AddRange([12, 13]);
        }
        titulos.Add("Observaciones");
        anchos.Add(50);
        return new Hoja("Gimnasio", titulos.ToArray(), anchos.ToArray(), filas);
    }

    private static async Task<Dictionary<ulong, Dictionary<string, Medicion>>> MedicionesAsync(MySqlConnection cn, string? codigo, ulong? sesion)
    {
        await using var cmd = new MySqlCommand(
            "SELECT m.sesion_id, m.momento, m.fc, m.spo2, m.borg, m.pa_sistolica, m.pa_diastolica " +
            Desde + "JOIN marcha_mediciones m ON m.sesion_id = s.id " + Donde, cn);
        Filtro(cmd, codigo, sesion);
        await using var r = await cmd.ExecuteReaderAsync();
        var d = new Dictionary<ulong, Dictionary<string, Medicion>>();
        while (await r.ReadAsync())
        {
            ulong id = Convert.ToUInt64(r.GetValue(0));
            if (!d.TryGetValue(id, out var porMomento)) d[id] = porMomento = [];
            porMomento[r.GetString(1)] = Mediciones.Leer(r, 2);
        }
        return d;
    }

    private static async Task<Dictionary<ulong, List<decimal>>> MinutosAsync(MySqlConnection cn, string? codigo, ulong? sesion)
    {
        await using var cmd = new MySqlCommand(
            "SELECT m.sesion_id, m.distancia_m " +
            Desde + "JOIN caminata_minutos m ON m.sesion_id = s.id " + Donde +
            "ORDER BY m.sesion_id, m.minuto", cn);
        Filtro(cmd, codigo, sesion);
        await using var r = await cmd.ExecuteReaderAsync();
        var d = new Dictionary<ulong, List<decimal>>();
        while (await r.ReadAsync())
        {
            ulong id = Convert.ToUInt64(r.GetValue(0));
            if (!d.TryGetValue(id, out var lista)) d[id] = lista = [];
            lista.Add(Convert.ToDecimal(r.GetValue(1)));
        }
        return d;
    }

    private static void Filtro(MySqlCommand cmd, string? codigo, ulong? sesion)
    {
        cmd.Parameters.AddWithValue("@codigo", (object?)codigo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@sesion", (object?)sesion ?? DBNull.Value);
    }
}
