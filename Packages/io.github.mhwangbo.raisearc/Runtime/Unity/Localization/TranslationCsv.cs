using System;
using System.Collections.Generic;
using System.Text;
using PrincessStudio.Core;

namespace PrincessStudio.Unity
{
    public static class TranslationCsv
    {
        public static string Export(ProjectDefinition project)
        {
            var text = new StringBuilder("key,locale,text,draft\r\n");
            foreach (var entry in project.translations)
                text.Append(Cell(entry.key)).Append(',').Append(Cell(entry.locale)).Append(',').Append(Cell(entry.text)).Append(',').Append(entry.draft ? "true" : "false").Append("\r\n");
            return text.ToString();
        }
        public static void Import(AuthoringService service, string csv, int expectedRevision = -1)
        {
            if (csv == null || csv.Length > UnityProjectCodec.MaxJsonCharacters)
                throw new ArgumentException("CSV size limit exceeded.");
            var rows = Parse(csv);
            if (rows.Count == 0 || rows[0].Count != 4 || rows[0][0].TrimStart('\uFEFF') != "key" || rows[0][1] != "locale" || rows[0][2] != "text" || rows[0][3] != "draft")
                throw new ArgumentException("CSV header must be key,locale,text,draft.");
            service.Edit(p =>
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 1; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row.Count == 1 && row[0].Length == 0)
                        continue;
                    if (row.Count != 4 || !bool.TryParse(row[3], out var draft) || !seen.Add(row[1] + ":" + row[0]))
                        throw new ArgumentException("Invalid or duplicate CSV row: " + (i + 1));
                    p.translations.RemoveAll(t => t.key == row[0] && t.locale == row[1]);
                    p.translations.Add(new TranslationEntry { key = row[0], locale = row[1], text = row[2], draft = draft });
                }
            }, expectedRevision);
        }
        private static string Cell(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        private static List<List<string>> Parse(string csv)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false, closed = false;
            for (var i = 0; i < csv.Length; i++)
            {
                var c = csv[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                            closed = true;
                        }
                    }
                    else
                        field.Append(c);
                    continue;
                }
                if (c == '"')
                {
                    if (field.Length > 0 || closed)
                        throw new ArgumentException("Malformed CSV quote.");
                    quoted = true;
                    continue;
                }
                if (c == ',' || c == '\n' || c == '\r')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    closed = false;
                    if (c != ',')
                    {
                        rows.Add(row);
                        row = new List<string>();
                        if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                            i++;
                    }
                    continue;
                }
                if (closed)
                    throw new ArgumentException("Unexpected text after closing quote.");
                field.Append(c);
            }
            if (quoted)
                throw new ArgumentException("Unterminated CSV quote.");
            if (field.Length > 0 || row.Count > 0 || closed)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
