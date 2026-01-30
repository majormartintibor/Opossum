namespace Opossum.Core;

/// <summary>
/// Options for reading events from the event store
/// </summary>
[Flags]
public enum ReadOption
{
    /// <summary>
    /// Default read behavior - events in ascending sequence order (chronological)
    /// </summary>
    None = 0,

    /// <summary>
    /// Read events in descending sequence order (reverse chronological - latest first)
    /// </summary>
    Descending = 1
}
