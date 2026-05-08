using System;
using UglyToad.PdfPig;
using System.IO;

class Program
{
    static void Main()
    {
        string pdfPath = @"r:\VKD Petroleum\215436_report_fieldofficer.pdf";
        using var doc = PdfDocument.Open(pdfPath);
        foreach (var page in doc.GetPages())
        {
            Console.WriteLine("PAGE " + page.Number);
            Console.WriteLine(page.Text);
        }
    }
}
