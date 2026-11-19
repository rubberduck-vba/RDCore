using System.Runtime.CompilerServices;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.LanguageServer.Parsing;

// the JSON a parse came in as from the parser, which is what goes to the host for that parse: writing the same tree again costs as much as the parser spent
// writing it. A parse is known by its instance. The language server changes a parse only by copying it ('with'), and a copy did not come in as any JSON,
// so it is written again. A parse is kept by its own lifetime: once it is let go, its JSON goes with it.
internal static class ParserJson
{
    private static readonly ConditionalWeakTable<ModuleParseResult, string> _received = [];

    // the parse the parser answered, its JSON kept for it.
    internal static ModuleParseResult Unwrap(PlatformJsonEnvelope envelope)
    {
        var parse = envelope.Unwrap<ModuleParseResult>();
        _received.AddOrUpdate(parse, envelope.Json);
        return parse;
    }

    // the JSON of a parse as the parser answered it, or written from the parse when the parser did not answer that one.
    internal static string Of(ModuleParseResult parse)
        => _received.TryGetValue(parse, out var json) ? json : PlatformJson.Serialize(parse);
}
