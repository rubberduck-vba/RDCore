using CommandLine;

namespace RDCore.SDK.Server.Configuration;

/// <summary>
/// Parses a platform application's command line into <see cref="SdkAppCommandLineArgs"/>.
/// </summary>
/// <remarks>
/// Every platform application parses the same arguments the same way, and every one of them has to tell
/// a command line that <em>asked a question</em> apart from one that configures a run: the parser answers
/// <c>--help</c> and <c>--version</c> by writing the text itself, then reports the request as a parse
/// error and hands back a default instance. Reading that instance as configuration is what made
/// <c>rdc --help</c> print its help, then a stack trace underneath it, and exit -1.
/// </remarks>
public static class SdkCommandLine
{
    /// <summary>
    /// Parses <paramref name="args"/> as <see cref="SdkAppCommandLineArgs"/>.
    /// </summary>
    /// <param name="args">The command-line arguments as received.</param>
    /// <returns>The parsed arguments.</returns>
    public static SdkAppCommandLineArgs Parse(string[] args)
        => Parser.Default.ParseArguments<SdkAppCommandLineArgs>(args).Value;

    /// <summary>
    /// Answers <paramref name="args"/> if all it asked for is the help or version text.
    /// </summary>
    /// <param name="args">The command-line arguments as received.</param>
    /// <returns>
    /// <c>true</c> if the text was written and there is nothing to run; <c>false</c> if
    /// <paramref name="args"/> is a command line that configures a run, which the caller goes on to
    /// <see cref="Parse(string[])"/>.
    /// </returns>
    /// <remarks>
    /// Writing the text is the parser's own side effect of being asked. It reports the request as a parse
    /// <em>error</em>, because there is nothing left for it to parse — not because anything is wrong with
    /// what it was given, which is why an application asks this before it decides it has failed.
    /// </remarks>
    public static bool TryAnswerTextOnlyRequest(string[] args)
        => Parser.Default.ParseArguments<SdkAppCommandLineArgs>(args).Errors
            .Any(error => error.Tag is ErrorType.HelpRequestedError
                or ErrorType.HelpVerbRequestedError
                or ErrorType.VersionRequestedError);
}
