namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>
/// The role a <see cref="ConsoleMessagePart"/> plays within a message. A renderer styles each role
/// independently.
/// </summary>
public enum MessagePart
{
    /// <summary>Reserved for an overlay region (unused).</summary>
    Overlay,
    /// <summary>A timestamp shown ahead of the message.</summary>
    Timestamp,
    /// <summary>A short title / code shown ahead of the body.</summary>
    Title,
    /// <summary>The message text.</summary>
    Body,
    /// <summary>Additional detail shown only in verbose mode.</summary>
    Verbose,
    /// <summary>A substituted value span within the body (e.g. a count or duration).</summary>
    Metric,
    /// <summary>An exception stack trace.</summary>
    StackTrace,
}
