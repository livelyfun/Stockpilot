using System.Globalization;
using System.Text;

namespace StockPilot.Web.Infrastructure;

/// <summary>Minimal CSV writer used by report exports (RFC 4180 quoting).</summary>
public static class CsvExport
{
    public static byte[] Build(IEnumerable<string> headers, IEnumerable<IEnumerable<object?>> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', headers.Select(Escape)));

        foreach (var row in rows)
        {
            sb.AppendLine(string.Join(',', row.Select(Escape)));
        }

        // UTF-8 BOM so spreadsheet software detects the encoding correctly.
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string FormatQuantity(decimal value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    public static string FormatDate(DateTime value) => value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    private static string Escape(object? value)
    {
        var text = value switch
        {
            null => string.Empty,
            DateTime dt => FormatDate(dt),
            decimal d => d.ToString("0.####", CultureInfo.InvariantCulture),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };

        // Prevent spreadsheet formula injection from free-text columns.
        if (text.Length > 0 && "=+-@".Contains(text[0]) && !decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
        {
            text = "'" + text;
        }

        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
        {
            return '"' + text.Replace("\"", "\"\"") + '"';
        }

        return text;
    }
}
