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
            "c.distancia_m, c.num_paradas, o.texto, c.tiempo_detenido_s " + Desde +
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
                                         "Tiempo de exposición (min)", "Distancia (m)", "Veces que se detuvo", "Tiempo detenido (s)" };
        var anchos = new List<double> { 13, 11, 12, 8, 14, 18, 14, 12, 12, 12 };
        for (int i = 1; i <= columnas; i++) { titulos.Add($"Minuto {i} (m)"); anchos.Add(11); }
        foreach (var mo in Mediciones.Cronologico)
        {
            titulos.AddRange([$"{mo.Nombre} FC (lpm)", $"{mo.Nombre} SpO₂ (%)", $"{mo.Nombre} Borg disnea"]);
            if (mo.ConPa) titulos.AddRange([$"{mo.Nombre} PA sistólica (mmHg)", $"{mo.Nombre} PA diastólica (mmHg)"]);
        }
        while (anchos.Count < titulos.Count) anchos.Add(12);
        titulos.Add("Observaciones");
        anchos.Add(50);

        return Excel.Crear([new Hoja("Marcha", titulos.ToArray(), anchos.ToArray(), filas)]);
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
