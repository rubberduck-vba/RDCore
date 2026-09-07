namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>
/// One styled component of a console message. Concrete parts (title, body, timestamp, verbose, stack
/// trace, metric) carry their own value; a renderer maps <see cref="Part"/> to a style.
/// </summary>
/// <param name="Part">The role this part plays in the message.</param>
/// <param name="Value">The rendered text of this part.</param>
public abstract record class ConsoleMessagePart(MessagePart Part, string Value);
