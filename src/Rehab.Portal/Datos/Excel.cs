using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace Rehab.Portal.Datos;

// Estilos: 0 normal, 1 título, 2 fecha, 3 hora, 4 dos decimales, 5 un decimal, 6 texto largo
public sealed record Celda(object? Valor, int Estilo = 0);

public sealed record Hoja(string Nombre, string[] Titulos, double[] Anchos, List<Celda[]> Filas);

// Archivo .xlsx (SpreadsheetML, ECMA-376) sin dependencias externas
public static class Excel
{
    public const string Tipo = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static byte[] Crear(IReadOnlyList<Hoja> hojas)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var tipos = new StringBuilder(
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
            var libro = new StringBuilder(
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");
            var relaciones = new StringBuilder(
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

            for (int i = 1; i <= hojas.Count; i++)
            {
                tipos.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                libro.Append($"<sheet name=\"{Esc(NombreHoja(hojas[i - 1].Nombre))}\" sheetId=\"{i}\" r:id=\"rId{i}\"/>");
                relaciones.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
                Escribir(zip, $"xl/worksheets/sheet{i}.xml", HojaXml(hojas[i - 1]));
            }
            relaciones.Append($"<Relationship Id=\"rId{hojas.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");

            Escribir(zip, "[Content_Types].xml", tipos.Append("</Types>").ToString());
            Escribir(zip, "_rels/.rels",
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");
            Escribir(zip, "xl/workbook.xml", libro.Append("</sheets></workbook>").ToString());
            Escribir(zip, "xl/_rels/workbook.xml.rels", relaciones.Append("</Relationships>").ToString());
            Escribir(zip, "xl/styles.xml", Estilos);
        }
        return ms.ToArray();
    }

    private const string Estilos =
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<numFmts count=\"4\">" +
        "<numFmt numFmtId=\"164\" formatCode=\"dd/mm/yyyy\"/><numFmt numFmtId=\"165\" formatCode=\"hh:mm\"/>" +
        "<numFmt numFmtId=\"166\" formatCode=\"0.00\"/><numFmt numFmtId=\"167\" formatCode=\"0.0\"/></numFmts>" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts>" +
        "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE8EFF7\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
        "<border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FF9AA8B6\"/></bottom><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"7\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"167\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
        "</cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    private static string HojaXml(Hoja h)
    {
        var x = new StringBuilder("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        x.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
        x.Append("<cols>");
        for (int c = 0; c < h.Titulos.Length; c++)
        {
            double ancho = c < h.Anchos.Length ? h.Anchos[c] : 12;
            x.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{N(ancho)}\" customWidth=\"1\"/>");
        }
        x.Append("</cols><sheetData>");

        Fila(x, 1, h.Titulos.Select(t => new Celda(t, 1)).ToArray());
        for (int f = 0; f < h.Filas.Count; f++) Fila(x, f + 2, h.Filas[f]);

        // Sin triángulo verde en códigos que parecen números
        x.Append("</sheetData><ignoredErrors><ignoredError sqref=\"A1:XFD1048576\" numberStoredAsText=\"1\"/></ignoredErrors>");
        return x.Append("</worksheet>").ToString();
    }

    private static void Fila(StringBuilder x, int n, Celda[] celdas)
    {
        x.Append($"<row r=\"{n}\">");
        for (int c = 0; c < celdas.Length; c++)
        {
            var (valor, estilo) = (celdas[c].Valor, celdas[c].Estilo);
            string r = Columna(c) + n;
            string s = estilo > 0 ? $" s=\"{estilo}\"" : "";
            switch (valor)
            {
                case null:
                    if (estilo > 0) x.Append($"<c r=\"{r}\"{s}/>");
                    break;
                case string t:
                    x.Append($"<c r=\"{r}\"{s} t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(t)}</t></is></c>");
                    break;
                case DateTime d:
                    x.Append($"<c r=\"{r}\"{s}><v>{N(d.ToOADate())}</v></c>");
                    break;
                case TimeSpan ts:
                    x.Append($"<c r=\"{r}\"{s}><v>{N(ts.TotalDays)}</v></c>");
                    break;
                default:
                    x.Append($"<c r=\"{r}\"{s}><v>{N(Convert.ToDouble(valor, CultureInfo.InvariantCulture))}</v></c>");
                    break;
            }
        }
        x.Append("</row>");
    }

    // 0 -> A, 25 -> Z, 26 -> AA
    private static string Columna(int i)
    {
        string s = "";
        for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
        return s;
    }

    private static string NombreHoja(string n)
    {
        var limpio = new string(n.Where(ch => "[]:*?/\\".IndexOf(ch) < 0).ToArray()).Trim();
        return limpio.Length == 0 ? "Hoja" : limpio[..Math.Min(31, limpio.Length)];
    }

    // Quita caracteres no válidos en XML
    private static string Esc(string t) =>
        SecurityElement.Escape(new string(t.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ').ToArray()))!;

    private static string N(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    private static void Escribir(ZipArchive zip, string ruta, string xml)
    {
        using var w = new StreamWriter(zip.CreateEntry(ruta, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        w.Write("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        w.Write(xml);
    }
}
