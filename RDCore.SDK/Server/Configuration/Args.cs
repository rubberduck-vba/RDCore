using CommandLine;

namespace RDCore.SDK.Server.Configuration;

public static class Args
{
    public static SdkServerOptions Parse(string[] args)
    {
        var parser = new Parser(config =>
        {
            config.CaseInsensitiveEnumValues = true;
            config.HelpWriter = Console.Out;
        });
        var result = parser.ParseArguments<SdkServerOptions>(args);
        if (result.Tag == ParserResultType.Parsed)
        {
            return result.Value;
        }
        else
        {
            throw new ArgumentException("Failed to parse command-line arguments.");
        }
    }
}