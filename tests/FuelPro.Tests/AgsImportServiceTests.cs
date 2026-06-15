using System.IO;
using FuelPro.Core.Services;

namespace FuelPro.Tests;

public class AgsImportServiceTests
{
    [Fact]
    public async Task ParseAgsReport_ShouldParseAllNozzleOpeningAndClosingValues_FromReferencePdf()
    {
        var sut = new AgsImportService();
        string pdfPath;
        try
        {
            pdfPath = ResolveReferencePdfPath();
        }
        catch (FileNotFoundException)
        {
            // Skip the test if the reference PDF is missing from the environment
            return;
        }

        var dto = await sut.ParseAgsReportAsync(pdfPath, new DateTime(2026, 4, 20), "A");

        Assert.Equal(28, dto.NozzleReadings.Count);

        var readingsByNozzle = dto.NozzleReadings.ToDictionary(r => r.NozzleNumber);
        foreach (var expected in ExpectedReadingsByNozzle)
        {
            Assert.True(readingsByNozzle.TryGetValue(expected.Key, out var parsed), $"Nozzle {expected.Key} was not parsed.");
            Assert.Equal(expected.Value.Opening, parsed!.OpeningReading, 2);
            Assert.Equal(expected.Value.Closing, parsed.ClosingReading, 2);
            Assert.True(parsed.ClosingReading >= parsed.OpeningReading, $"Nozzle {expected.Key} has closing reading lower than opening reading.");
        }
    }

    private static string ResolveReferencePdfPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, "215436_report_fieldofficer.pdf");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate 215436_report_fieldofficer.pdf from the test output directory.");
    }

    private static readonly IReadOnlyDictionary<int, (double Opening, double Closing)> ExpectedReadingsByNozzle
        = new Dictionary<int, (double Opening, double Closing)>
        {
            { 1, (6396158.60, 6396609.36) },
            { 2, (6187607.86, 6188074.46) },
            { 3, (745158.91, 745163.91) },
            { 4, (1200487.96, 1200492.96) },
            { 5, (713710.43, 713774.77) },
            { 6, (1434468.47, 1434629.28) },
            { 7, (708085.17, 708090.17) },
            { 8, (2444271.39, 2444276.39) },
            { 9, (1653829.24, 1654160.46) },
            { 10, (1420829.08, 1420901.81) },
            { 11, (1219333.78, 1219338.78) },
            { 12, (1093004.42, 1093009.42) },
            { 13, (1540891.84, 1540896.84) },
            { 14, (909259.46, 909264.46) },
            { 15, (1305606.59, 1305611.59) },
            { 16, (1239451.21, 1239456.21) },
            { 17, (2125848.20, 2126104.28) },
            { 18, (1876021.51, 1876026.51) },
            { 19, (1581586.37, 1581591.37) },
            { 20, (2293388.34, 2293393.34) },
            { 21, (1495433.13, 1495433.13) },
            { 22, (760179.21, 760184.21) },
            { 23, (1804230.18, 1804235.18) },
            { 24, (1108859.75, 1108869.75) },
            { 25, (1164653.63, 1164658.63) },
            { 26, (338812.38, 338817.38) },
            { 27, (1446041.93, 1446046.93) },
            { 28, (439770.83, 439775.83) }
        };
}
