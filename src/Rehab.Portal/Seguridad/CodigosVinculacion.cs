// Mismo formato que la API: 8 caracteres Crockford Base32; en la base solo su hash.
using System.Security.Cryptography;
using System.Text;

namespace Rehab.Portal.Seguridad;

public static class CodigosVinculacion
{
    private const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private const int Longitud = 8;

    // Acepta minúsculas, guiones y espacios; corrige O→0 e I/L→1.
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

    public static byte[] Hash(string normalizado) => SHA256.HashData(Encoding.ASCII.GetBytes(normalizado));
}
