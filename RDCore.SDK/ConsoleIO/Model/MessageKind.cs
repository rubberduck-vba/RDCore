namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>
/// The severity / intent of a console message. Selects the icon and accent style a renderer applies.
/// </summary>
public enum MessageKind
{
    /// <summary>Diagnostic detail; the lowest-priority kind.</summary>
    Trace,
    /// <summary>Neutral progress or status information.</summary>
    Information,
    /// <summary>A recoverable problem the user should notice.</summary>
    Warning,
    /// <summary>A failure.</summary>
    Error,
    /// <summary>A completed operation worth confirming.</summary>
    Success,
}
