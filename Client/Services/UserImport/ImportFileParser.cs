using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace AuthWithAdmin.Client.Services.UserImport;

/// <summary>
/// Turns an uploaded spreadsheet into a header row + data rows. Nothing in here
/// knows about users — it produces a rectangular grid of strings and stops.
///
/// <para>TWO FORMATS, TWO REASONS. Delimited text (.csv/.tsv/.txt) is parsed by
/// the state machine below because the stack has no CSV package and the format
/// does not need one. .xlsx is read straight out of its OOXML package with
/// <see cref="ZipArchive"/> + <see cref="XDocument"/>, both BCL — also no new
/// dependency. There is deliberately NO .xls support: the pre-2007 binary
/// format is not a zip and cannot be read without a real library.</para>
///
/// <para>The xlsx reader handles what a user list actually contains — shared
/// strings, inline strings, plain numbers and booleans. It does NOT interpret
/// number formats, so a cell Excel is displaying as a date arrives as its
/// serial number. That is acceptable here precisely because no field this
/// import writes is a date; if a date field is ever added, this is the place
/// that has to learn about styles.xml first.</para>
/// </summary>
public static class ImportFileParser
{
    /// <summary>Extensions the upload control accepts, as an accept="" list.</summary>
    public const string AcceptAttribute = ".csv,.tsv,.txt,.xlsx";

    public sealed record Grid(IReadOnlyList<string> Headers,
                              IReadOnlyList<IReadOnlyList<string>> Rows,
                              string FormatLabel,
                              string? Warning);

    public sealed class ParseException : Exception
    {
        public ParseException(string message) : base(message) { }
    }

    public static Grid Parse(string fileName, byte[] bytes)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();

        List<IReadOnlyList<string>> cells;
        string? warning = null;

        switch (ext)
        {
            case ".xlsx":
                cells = ReadXlsx(bytes);
                break;
            case ".csv":
            case ".tsv":
            case ".txt":
                cells = ReadDelimited(bytes, out warning);
                break;
            case "":
                throw new ParseException("לקובץ אין סיומת — נדרש קובץ CSV או XLSX.");
            default:
                throw new ParseException($"סוג הקובץ {ext} אינו נתמך. יש להעלות קובץ CSV או XLSX.");
        }

        // Drop rows that are entirely blank — a spreadsheet exported from Excel
        // routinely carries a tail of them and they are not "invalid rows", they
        // are not rows at all.
        var nonEmpty = cells
            .Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c)))
            .ToList();

        if (nonEmpty.Count == 0)
            throw new ParseException("הקובץ ריק — לא נמצאו שורות.");

        var rawHeaders = nonEmpty[0];
        var headers = new List<string>();
        for (var i = 0; i < rawHeaders.Count; i++)
        {
            var h = (rawHeaders[i] ?? "").Trim();
            // A blank header still occupies a column, and its data must stay
            // addressable — otherwise every later column shifts by one.
            headers.Add(h.Length == 0 ? $"עמודה {i + 1}" : h);
        }

        var rows = new List<IReadOnlyList<string>>();
        foreach (var r in nonEmpty.Skip(1))
        {
            // Pad/trim to the header width so downstream code can index by
            // column position without a bounds check on every access.
            var row = new string[headers.Count];
            for (var i = 0; i < headers.Count; i++)
                row[i] = i < r.Count ? (r[i] ?? "").Trim() : "";
            rows.Add(row);
        }

        var label = ext == ".xlsx" ? "Excel (XLSX)" : ext.TrimStart('.').ToUpperInvariant();
        return new Grid(headers, rows, label, warning);
    }

    // ── Delimited text ──────────────────────────────────────────────────────

    private static List<IReadOnlyList<string>> ReadDelimited(byte[] bytes, out string? warning)
    {
        warning = null;

        // UTF-8 with BOM detection. Excel on Windows still writes Hebrew CSV as
        // windows-1255 by default, and that code page is NOT available in the
        // WASM runtime without System.Text.Encoding.CodePages — so rather than
        // mis-decode silently, decode as UTF-8 and say so when it clearly was
        // not UTF-8 (the replacement character only appears on invalid bytes).
        var text = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetString(bytes)
            .TrimStart('﻿');

        if (text.Contains('�'))
        {
            warning = "נראה שהקובץ אינו מקודד ב-UTF-8, וייתכן שתווים בעברית ייקראו שגויים. " +
                      "ב-Excel יש לשמור כ-\"CSV UTF-8\", או להעלות קובץ XLSX.";
        }

        var delimiter = DetectDelimiter(text);
        return ParseDelimited(text, delimiter);
    }

    /// <summary>Picks between comma, semicolon and tab by counting separators
    /// OUTSIDE quotes on the first line. A Hebrew Windows Excel writes
    /// semicolon-separated CSV, so assuming a comma would produce a single
    /// column and read as "the file has no columns".</summary>
    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n', 2)[0];

        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 };
        var inQuotes = false;

        foreach (var ch in firstLine)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (inQuotes) continue;
            if (counts.ContainsKey(ch)) counts[ch]++;
        }

        var best = counts.OrderByDescending(kv => kv.Value).First();
        return best.Value > 0 ? best.Key : ',';
    }

    /// <summary>RFC 4180: quoted fields may contain the delimiter, newlines, and
    /// doubled quotes. Written as an explicit state machine rather than
    /// Split(delimiter) because an email column is exactly where a naive split
    /// starts corrupting rows.</summary>
    private static List<IReadOnlyList<string>> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<IReadOnlyList<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }

            if (ch == '"')      { inQuotes = true; }
            else if (ch == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r') { /* handled by \n; a lone \r is treated as EOL below */
                if (i + 1 >= text.Length || text[i + 1] != '\n')
                {
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = new List<string>();
                }
            }
            else if (ch == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = new List<string>();
            }
            else field.Append(ch);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    // ── XLSX (OOXML) ────────────────────────────────────────────────────────

    private static List<IReadOnlyList<string>> ReadXlsx(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);

            var shared = ReadSharedStrings(zip);
            var sheetPath = ResolveFirstSheetPath(zip);

            var sheetEntry = zip.GetEntry(sheetPath)
                ?? throw new ParseException("לא נמצא גיליון בקובץ ה-Excel.");

            using var sheetStream = sheetEntry.Open();
            var doc = XDocument.Load(sheetStream);

            var rows = new List<IReadOnlyList<string>>();

            foreach (var rowEl in doc.Descendants().Where(e => e.Name.LocalName == "row"))
            {
                var cells = new List<string>();

                foreach (var cEl in rowEl.Elements().Where(e => e.Name.LocalName == "c"))
                {
                    // The r="B7" reference is authoritative: Excel omits empty
                    // cells entirely, so reading them in document order without
                    // this would silently shift every value left.
                    var reference = (string?)cEl.Attribute("r");
                    var colIndex = reference is null ? cells.Count : ColumnIndex(reference);

                    while (cells.Count < colIndex) cells.Add("");

                    var value = CellValue(cEl, shared);
                    if (cells.Count == colIndex) cells.Add(value);
                    else cells[colIndex] = value;
                }

                rows.Add(cells);
            }

            return rows;
        }
        catch (ParseException) { throw; }
        catch (InvalidDataException)
        {
            throw new ParseException(
                "הקובץ אינו קובץ XLSX תקין. אם מדובר בקובץ .xls ישן — יש לשמור אותו מחדש כ-XLSX או כ-CSV.");
        }
        catch (Exception)
        {
            throw new ParseException(
                "קריאת קובץ ה-Excel נכשלה. ניתן לשמור את הגיליון כ-\"CSV UTF-8\" ולהעלות אותו במקום.");
        }
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return new List<string>();

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);

        // An <si> can be one <t> or several <r><t> runs (mixed formatting);
        // concatenating every descendant <t> gives the displayed string in
        // both cases.
        return doc.Root?
            .Elements().Where(e => e.Name.LocalName == "si")
            .Select(si => string.Concat(
                si.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value)))
            .ToList() ?? new List<string>();
    }

    /// <summary>Resolves the FIRST sheet through workbook.xml + its rels rather
    /// than assuming xl/worksheets/sheet1.xml — the file names do not have to
    /// match the sheet order, and a workbook whose first tab is "sheet3.xml" is
    /// perfectly legal.</summary>
    private static string ResolveFirstSheetPath(ZipArchive zip)
    {
        const string fallback = "xl/worksheets/sheet1.xml";

        var wbEntry = zip.GetEntry("xl/workbook.xml");
        if (wbEntry is null) return fallback;

        using var wbStream = wbEntry.Open();
        var wb = XDocument.Load(wbStream);

        var firstSheet = wb.Descendants().FirstOrDefault(e => e.Name.LocalName == "sheet");
        var relId = firstSheet?.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
        if (relId is null) return fallback;

        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry is null) return fallback;

        using var relsStream = relsEntry.Open();
        var rels = XDocument.Load(relsStream);

        var target = rels.Descendants()
            .Where(e => e.Name.LocalName == "Relationship")
            .FirstOrDefault(e => (string?)e.Attribute("Id") == relId)
            ?.Attribute("Target")?.Value;

        if (string.IsNullOrWhiteSpace(target)) return fallback;

        target = target.TrimStart('/');
        return target.StartsWith("xl/", StringComparison.Ordinal) ? target : "xl/" + target;
    }

    private static string CellValue(XElement cell, List<string> shared)
    {
        var type = (string?)cell.Attribute("t");

        if (type == "inlineStr")
        {
            return string.Concat(cell.Descendants()
                .Where(t => t.Name.LocalName == "t")
                .Select(t => t.Value));
        }

        var v = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
        if (v is null) return "";

        return type switch
        {
            "s" => int.TryParse(v, out var idx) && idx >= 0 && idx < shared.Count ? shared[idx] : "",
            "b" => v == "1" ? "TRUE" : "FALSE",
            _   => v,   // "n", "str" (cached formula result) and untyped all arrive as text
        };
    }

    /// <summary>"B7" → 1. Letters only; the row part is ignored.</summary>
    private static int ColumnIndex(string reference)
    {
        var n = 0;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z') n = n * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') n = n * 26 + (ch - 'a' + 1);
            else break;
        }
        return Math.Max(0, n - 1);
    }
}
