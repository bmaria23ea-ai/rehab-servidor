namespace Rehab.Portal.Datos;

// Ecuador continental: UTC-5 todo el año.
public static class Hora
{
    private static readonly TimeSpan Desfase = TimeSpan.FromHours(-5);

    public static DateTime Ecuador(DateTime utc) => utc + Desfase;
    public static DateOnly HoyEcuador() => DateOnly.FromDateTime(DateTime.UtcNow + Desfase);
    public static string Fecha(DateTime? utc) => utc is { } u ? Ecuador(u).ToString("dd/MM/yyyy") : "-";
    public static string Dia(DateTime? fecha) => fecha?.ToString("dd/MM/yyyy") ?? "-";   // fechas sin hora, sin convertir
    public static string FechaHora(DateTime? utc) => utc is { } u ? Ecuador(u).ToString("dd/MM/yyyy HH:mm") : "-";
}
