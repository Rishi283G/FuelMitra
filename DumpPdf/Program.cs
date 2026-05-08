using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

class Program
{
    static void Main()
    {
        string pdfPath   = @"r:\VKD Petroleum\215436_report_fieldofficer.pdf";
        string outDir    = @"r:\VKD Petroleum";
        string coordFile = Path.Combine(outDir, "ags_coord_dump.txt");
        string rawFile   = Path.Combine(outDir, "ags_raw_dump.txt");

        var coordSb = new StringBuilder();
        var rawSb   = new StringBuilder();

        using var doc = PdfDocument.Open(pdfPath);
        foreach (var page in doc.GetPages())
        {
            // ── Coordinate dump ──────────────────────────────────────────────
            coordSb.AppendLine($"=== PAGE {page.Number} ===");
            coordSb.AppendLine("--- WORDS WITH X/Y COORDS ---");

            var words = page.GetWords()
                .OrderByDescending(w => w.BoundingBox.Bottom)
                .ThenBy(w => w.BoundingBox.Left)
                .ToList();

            foreach (var w in words)
                coordSb.AppendLine(
                    $"[X:{w.BoundingBox.Left,7:F1} Y:{w.BoundingBox.Bottom,7:F1}] '{w.Text}'");

            coordSb.AppendLine();
            coordSb.AppendLine("--- ROW-GROUPED (Y±3) ---");
            var rows = GroupWordsIntoRows(page);
            foreach (var row in rows)
                coordSb.AppendLine(row);

            coordSb.AppendLine();

            // ── Raw page.Text ─────────────────────────────────────────────────
            rawSb.AppendLine($"=== PAGE {page.Number} ===");
            rawSb.AppendLine(page.Text);
            rawSb.AppendLine();
        }

        File.WriteAllText(coordFile, coordSb.ToString(), Encoding.UTF8);
        File.WriteAllText(rawFile,   rawSb.ToString(),   Encoding.UTF8);

        Console.WriteLine($"✅ Coord dump → {coordFile}");
        Console.WriteLine($"✅ Raw dump   → {rawFile}");
    }

    private static List<string> GroupWordsIntoRows(Page page, double yTolerance = 3.0)
    {
        var words = page.GetWords()
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var rows = new List<(double Y, List<Word> Words)>();
        foreach (var word in words)
        {
            double wordY = word.BoundingBox.Bottom;
            int idx = rows.FindIndex(r => Math.Abs(r.Y - wordY) <= yTolerance);
            if (idx < 0)
                rows.Add((wordY, new List<Word> { word }));
            else
            {
                rows[idx].Words.Add(word);
                rows[idx].Words.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
            }
        }
        return rows
            .OrderByDescending(r => r.Y)
            .Select(r => string.Join("  ", r.Words.Select(w => w.Text)))
            .ToList();
    }
}
