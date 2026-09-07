namespace RDCore.SDK.ConsoleIO.Model;

/// <summary>Additional detail rendered only when verbose output is enabled.</summary>
/// <param name="Verbose">The verbose text.</param>
public record class ConsoleMessageVerbosePart(string Verbose) : ConsoleMessagePart(MessagePart.Verbose, Verbose);

/// <summary>Creates <see cref="ConsoleMessageVerbosePart"/>s.</summary>
public static class ConsoleMessageVerbosePartFactory
{
    /// <summary>A verbose part from literal text.</summary>
    public static ConsoleMessagePart CreateVerbosePart(string verbose) => new ConsoleMessageVerbosePart(verbose);
}
