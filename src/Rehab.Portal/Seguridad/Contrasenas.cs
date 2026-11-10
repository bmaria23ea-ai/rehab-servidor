// Reglas según NIST SP 800-63B-4: longitud mínima, sin reglas de composición ni caducidad.
using System.Security.Cryptography;
using Microsoft.AspNetCore.Identity;

namespace Rehab.Portal.Seguridad;

public static class Contrasenas
{
    public const int Minimo = 10;
    public const int Maximo = 64;

    private const string Alfabeto = "23456789abcdefghjkmnpqrstuvwxyz";
    private static readonly object Usuario = new();
    private static readonly PasswordHasher<object> Hasher = new();

    private static readonly HashSet<string> Comunes = new(StringComparer.OrdinalIgnoreCase)
    {
        "1234567890", "0123456789", "12345678910", "1111111111", "0000000000", "abcdefghij",
        "qwertyuiop", "password123", "contraseña", "contrasena", "contraseña1", "contrasena1",
        "contraseña123", "contrasena123", "administrador", "terapeuta123", "rehabilitacion",
        "rehabilitación", "fisioterapia", "ecuador2026", "ambato2026", "bienvenido1",
    };

    public static string Temporal() =>
        string.Join('-', Enumerable.Range(0, 3).Select(_ => RandomNumberGenerator.GetString(Alfabeto, 4)));

    public static string Hash(string contrasena) => Hasher.HashPassword(Usuario, contrasena);

    // Para comparar contra algo cuando el usuario no existe y no revelar por el tiempo de respuesta.
    public static readonly string HashFalso = Hash(Temporal());

    public static PasswordVerificationResult Verificar(string hash, string contrasena) =>
        Hasher.VerifyHashedPassword(Usuario, hash, contrasena);

    public static string? Problema(string nueva, string usuario)
    {
        int largo = nueva.EnumerateRunes().Count();
        if (largo < Minimo) return $"Use al menos {Minimo} caracteres. Puede ser una frase.";
        if (largo > Maximo) return $"Use como máximo {Maximo} caracteres.";
        if (Comunes.Contains(nueva.Trim())) return "Esa contraseña es demasiado común.";
        if (nueva.Contains(usuario, StringComparison.OrdinalIgnoreCase)) return "No incluya su usuario en la contraseña.";
        return null;
    }

    public static string NuevoSello() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
