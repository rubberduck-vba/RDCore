using Newtonsoft.Json.Linq;
using RDCore.SDK.Server.Commands;
using RDCore.SDK.Server.Commands.Parsing;

namespace RDCore.Parsing.Commands;

internal class ParseDocumentParamsParser : ICommandParamsParser<ParseDocumentParams>
{
    public ParseDocumentParams? Parse(JArray? args)
    {
        if (args is JArray arr && arr.Count == 1)
        {
            var uriString = args[0].Value<string>();
            if (uriString is not null)
            {
                return new ParseDocumentParams { DocumentUri = new Uri(uriString) };
            }
        }
        return null;
    }
}

/// <summary>
/// Commands the <c>RDCore.Parser</c> server to parse the document at a <c>Uri</c>.
/// </summary>
/// <param name="argsParser">A service that parses the raw <c>JToken</c> parameters.</param>
public sealed class ParseDocumentCommand(ICommandParamsParser<ParseDocumentParams> argsParser) 
    : ServerCommand<ParseDocumentParams>(argsParser, SdkServerCommandNames.ParseDocument)
{
    protected override async Task ExecuteAsync(ParseDocumentParams? args, CancellationToken token)
    {
        if (args?.DocumentUri is Uri uri)
        {
            // TODO parse the document at the specified Uri
        }
        else
        {
            // TODO parse everything in the workspace?
        }
    }
}
