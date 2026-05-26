namespace RDCore.CLI.App.Messages.Model;

internal record class ConsoleMessageVerbosePart(string Verbose) : ConsoleMessagePart(MessagePart.Verbose, Verbose) { }
internal record class ConsoleMessageVerbosePartFactory
{
    public static ConsoleMessagePart CreateVerbosePart(string verbose) => new ConsoleMessageVerbosePart(verbose);
}
