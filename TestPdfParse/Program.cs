using System;
using System.Linq;
using UglyToad.PdfPig;
using System.Collections.Generic;

class Program
{
    static void Main()
    {
        string pdfPath = @"215436_report_fieldofficer.pdf";
        using var doc = PdfDocument.Open(pdfPath);
        foreach (var page in doc.GetPages())
        {
            var words = page.GetWords()
                .OrderByDescending(w => w.BoundingBox.Bottom)
                .ThenBy(w => w.BoundingBox.Left)
                .ToList();

            var rows = new List<RowData>();
            foreach (var word in words)
            {
                var y = word.BoundingBox.Bottom;
                var row = rows.FirstOrDefault(r => Math.Abs(r.Y - y) <= 3.0);
                if (row == null)
                {
                    row = new RowData { Y = y };
                    rows.Add(row);
                }
                row.Words.Add(word);
            }

            foreach (var row in rows)
            {
                row.Words = row.Words.OrderBy(w => w.BoundingBox.Left).ToList();
                string text = string.Join(" ", row.Words.Select(w => w.Text));
                if (text.Contains("TANK STOCK") || text.Contains("MS") || text.Contains("HSD"))
                    Console.WriteLine("ROW: "+ text);
            }
        }
    }
    
    private class RowData {
        public double Y;
        public List<UglyToad.PdfPig.Content.Word> Words = new List<UglyToad.PdfPig.Content.Word>();
    }
}
