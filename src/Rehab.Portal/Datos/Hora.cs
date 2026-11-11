namespace Rehab.Portal.Datos;

// Ecuador continental: UTC-5 todo el año.
public static class Hora
{
    private static readonly TimeSpan Desfase = TimeSpan.FromHours(-5);
    private static readonly string[] Dias = ["Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado"];
    private static readonly string[] Meses = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio",
                                              "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    public static DateTime Ecuador(DateTime utc) => utc + Desfase;
    public static DateOnly HoyEcuador() => DateOnly.FromDateTime(DateTime.UtcNow + Desfase);
    public static DateTime InicioHoyUtc() => HoyEcuador().ToDateTime(TimeOnly.MinValue) - Desfase;
    public static string Fecha(DateTime? utc) => utc is { } u ? Ecuador(u).ToString("dd/MM/yyyy") : "-";
    public static string Dia(DateTime? fecha) => fecha?.ToString("dd/MM/yyyy") ?? "-";   // fechas sin hora, sin convertir
    public static string FechaHora(DateTime? utc) => utc is { } u ? Ecuador(u).ToString("dd/MM/yyyy HH:mm") : "-";
    public static string SoloHora(DateTime utc) => Ecuador(utc).ToString("HH:mm");

    // "Hoy 15:10", "Ayer 09:30" o "08/11/2026 17:42"
    public static string Cercana(DateTime utc)
    {
        var dia = DateOnly.FromDateTime(Ecuador(utc));
        var hoy = HoyEcuador();
        string prefijo = dia == hoy ? "Hoy" : dia == hoy.AddDays(-1) ? "Ayer" : dia.ToString("dd/MM/yyyy");
        return $"{prefijo} {SoloHora(utc)}";
    }

    // "Miércoles 11 de noviembre"
    public static string HoyLargo()
    {
        var d = HoyEcuador();
        return $"{Dias[(int)d.DayOfWeek]} {d.Day} de {Meses[d.Month - 1]}";
    }
}
