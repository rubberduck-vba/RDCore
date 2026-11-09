namespace RDCore.CLI.ConsoleIO.Model;

public record class ConsoleMessageVerbosePart(string Verbose) : ConsoleMessagePart(MessagePart.Verbose, Verbose);

public static class ConsoleMessageVerbosePartFactory
{
    public static ConsoleMessagePart CreateVerbosePart(string verbose) => new ConsoleMessageVerbosePart(verbose);
}
