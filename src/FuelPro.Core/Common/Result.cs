namespace FuelPro.Core.Common;

/// <summary>
/// Generic result pattern for repository and service returns.
/// Never throws exceptions to the UI layer.
/// </summary>
public class Result<T>
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;
    public T? Data { get; init; }

    public static Result<T> Ok(T data) => new() { Success = true, Data = data };
    public static Result<T> Fail(string error) => new() { Success = false, Error = error };
}

public class Result
{
    public bool Success { get; init; }
    public string Error { get; init; } = string.Empty;

    public static Result Ok() => new() { Success = true };
    public static Result Fail(string error) => new() { Success = false, Error = error };
}
