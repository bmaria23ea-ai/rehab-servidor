// Claves de Data Protection en MariaDB: cifradas en reposo e incluidas en las copias de la base.
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;
using MySqlConnector;

namespace Rehab.Portal.Datos;

public sealed class ClavesMariaDb(MySqlDataSource ds) : IXmlRepository
{
    public IReadOnlyCollection<XElement> GetAllElements()
    {
        using var cn = ds.OpenConnection();
        using var cmd = new MySqlCommand("SELECT xml FROM portal_claves", cn);
        using var r = cmd.ExecuteReader();
        var claves = new List<XElement>();
        while (r.Read()) claves.Add(XElement.Parse(r.GetString(0)));
        return claves;
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        using var cn = ds.OpenConnection();
        using var cmd = new MySqlCommand("INSERT INTO portal_claves (nombre, xml) VALUES (@nombre, @xml)", cn);
        cmd.Parameters.AddWithValue("@nombre", friendlyName);
        cmd.Parameters.AddWithValue("@xml", element.ToString(SaveOptions.DisableFormatting));
        cmd.ExecuteNonQuery();
    }
}
