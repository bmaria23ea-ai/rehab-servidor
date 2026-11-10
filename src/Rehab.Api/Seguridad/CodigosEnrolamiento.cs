// Códigos de vinculación de visores (los muestran las gafas): 8 caracteres Crockford Base32 (40 bits),
// sin letras que se confunden (I, L, O, U). En la base solo se guarda su hash.
using System.Security.Cryptography;
using System.Text;

namespace Rehab.Api.Seguridad;

public static class CodigosEnrolamiento
{
    private const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Longitud = 8;

    // Código aleatorio con generador criptográfico
    public static string Nuevo() => RandomNumberGenerator.GetString(Alfabeto, Longitud);

    // Para mostrarlo: K7QM-4XPA
    public static string Formatear(string codigo) => $"{codigo[..4]}-{codigo[4..]}";

    // Acepta minúsculas, guiones y espacios; corrige O→0 e I/L→1. Devuelve null si no es válido.
    public static string? Normalizar(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada) || entrada.Length > 20) return null;

        var sb = new StringBuilder(Longitud);
        foreach (char ch in entrada.ToUpperInvariant())
        {
            if (ch is '-' or ' ') continue;
            char c = ch switch { 'O' => '0', 'I' or 'L' => '1', _ => ch };
            if (Alfabeto.IndexOf(c) < 0) return null;
            sb.Append(c);
        }
        return sb.Length == Longitud ? sb.ToString() : null;
    }

    // SHA-256 del código normalizado (32 bytes)
    public static byte[] Hash(string codigoNormalizado) =>
        SHA256.HashData(Encoding.ASCII.GetBytes(codigoNormalizado));
}
