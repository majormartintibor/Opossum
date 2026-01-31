namespace Opossum.Core;

/// <summary>
/// Represents the result of a command operation without a return value
/// </summary>
public record CommandResult(bool Success, string? ErrorMessage = null)
{
    /// <summary>
    /// Creates a successful command result
    /// </summary>
    public static CommandResult Ok() => new(true);

    /// <summary>
    /// Creates a failed command result with an error message
    /// </summary>
    public static CommandResult Fail(string errorMessage) => new(false, errorMessage);
}

/// <summary>
/// Represents the result of a command operation with a return value
/// </summary>
/// <typeparam name="T">The type of the return value</typeparam>
public record CommandResult<T>(bool Success, T? Value = default, string? ErrorMessage = null)
{
    /// <summary>
    /// Creates a successful command result with a value
    /// </summary>
    public static CommandResult<T> Ok(T value) => new(true, value);

    /// <summary>
    /// Creates a failed command result with an error message
    /// </summary>
    public static CommandResult<T> Fail(string errorMessage) => new(false, default, errorMessage);
}
