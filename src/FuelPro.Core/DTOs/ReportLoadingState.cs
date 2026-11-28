namespace FuelPro.Core.DTOs;

/// <summary>
/// Represents the asynchronous loading and validation lifecycle of a report.
/// Distinguishes valid empty data from loading in progress and query errors.
/// </summary>
public enum ReportLoadingState
{
    Empty,
    Loading,
    Loaded,
    Error
}

