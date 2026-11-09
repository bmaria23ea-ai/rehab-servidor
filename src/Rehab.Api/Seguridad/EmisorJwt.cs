// JWT de los visores: HS256, válidos 15 minutos. La clave de firma llega como Docker secret.
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Rehab.Api.Seguridad;

public sealed class EmisorJwt
{
    public const string Emisor = "rehab-api";
    public const string Audiencia = "rehab-visores";
    public const string ClaimVisor = "visor_id";
    public static readonly TimeSpan Duracion = TimeSpan.FromMinutes(15);

    private readonly SymmetricSecurityKey _clave;
    private readonly SigningCredentials _credenciales;
    private readonly JsonWebTokenHandler _handler = new();

    public EmisorJwt(byte[] clave)
    {
        if (clave.Length < 32)
            throw new InvalidOperationException("La clave JWT debe tener al menos 256 bits.");
        _clave = new SymmetricSecurityKey(clave);
        _credenciales = new SigningCredentials(_clave, SecurityAlgorithms.HmacSha256);
    }

    // Lee la clave (Base64) del archivo del Docker secret
    public static EmisorJwt DesdeArchivo(string ruta)
    {
        if (!File.Exists(ruta))
            throw new InvalidOperationException("No se encontró el archivo con la clave JWT.");
        return new EmisorJwt(Convert.FromBase64String(File.ReadAllText(ruta).Trim()));
    }

    // Token para un visor: sujeto, id del visor, identificador único y vencimiento
    public string Emitir(int visorId) => _handler.CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Emisor,
        Audience = Audiencia,
        Subject = new ClaimsIdentity(
        [
            new Claim(JwtRegisteredClaimNames.Sub, visorId.ToString()),
            new Claim(ClaimVisor, visorId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        ]),
        Expires = DateTime.UtcNow.Add(Duracion),
        SigningCredentials = _credenciales,
    });

    // Validación estricta: firma, emisor, audiencia, vencimiento y solo HS256
    public TokenValidationParameters ParametrosValidacion() => new()
    {
        ValidIssuer = Emisor,
        ValidAudience = Audiencia,
        IssuerSigningKey = _clave,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ClockSkew = TimeSpan.FromSeconds(30),
    };
}
