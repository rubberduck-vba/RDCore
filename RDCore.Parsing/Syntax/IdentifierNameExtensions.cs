using Antlr4.Runtime.Tree;
using System.Text;

namespace RDCore.Parsing.Syntax;

// null-tolerant identifier-name extraction from the grammar's identifier rules
// (identifier → (typed|untyped)Identifier → identifierValue → IDENTIFIER | keyword | foreignName).
// every accessor returns "" instead of throwing where LL recovery leaves an intermediate context
// null on a half-typed declaration. shape follows Rubberduck's Identifier.GetName (GPLv3).
internal static class IdentifierNameExtensions
{
    public static string Name(this VBAParser.IdentifierValueContext? context)
    {
        if (context is null)
        {
            return string.Empty;
        }
        if (context.foreignName() is { } foreign)
        {
            // '[' foreignIdentifier* ']' — the brackets are escape syntax, not part of the name.
            var builder = new StringBuilder();
            foreach (var part in foreign.foreignIdentifier())
            {
                builder.Append(part.GetText());
            }
            return builder.ToString();
        }
        var text = context.GetText();
        return IsRecoveryPlaceholder(text) ? string.Empty : text;
    }

    // ANTLR's DefaultErrorStrategy gives a synthetically inserted token the display text "<missing X>".
    internal static bool IsRecoveryPlaceholder(string? text)
        => text is null || text.StartsWith("<missing ", StringComparison.Ordinal);

    // null for a synthetic token ANTLR inserted during recovery (negative index, or a "<missing X>" body).
    internal static string? RealText(this ITerminalNode? node)
        => node is { Symbol.TokenIndex: >= 0 } && !IsRecoveryPlaceholder(node.GetText())
            ? node.GetText()
            : null;

    public static string Name(this VBAParser.IdentifierContext? context)
        => (context?.untypedIdentifier() ?? context?.typedIdentifier()?.untypedIdentifier())?.identifierValue().Name()
           ?? string.Empty;

    public static string Name(this VBAParser.UntypedIdentifierContext? context)
        => context?.identifierValue().Name() ?? string.Empty;

    // unrestrictedIdentifier is `identifier | statementKeyword | markerKeyword`; the keyword forms
    // have no identifier() child, so fall back to the raw text.
    public static string Name(this VBAParser.UnrestrictedIdentifierContext? context)
        => context is null
            ? string.Empty
            : context.identifier().Name() is { Length: > 0 } name ? name : context.GetText();

    public static string Name(this VBAParser.SubroutineNameContext? context)
        => context?.identifier().Name() ?? string.Empty;

    public static string Name(this VBAParser.FunctionNameContext? context)
        => context?.identifier().Name() ?? string.Empty;

    // a UDT field name is either the reserved-word form or the untyped form.
    public static string Name(this VBAParser.UdtMemberContext? context)
        => context?.reservedNameMemberDeclaration()?.unrestrictedIdentifier().Name() is { Length: > 0 } reserved
            ? reserved
            : context?.untypedNameMemberDeclaration()?.untypedIdentifier().Name() ?? string.Empty;

    // the type-declaration character (%, &, …) on a typed identifier, or null.
    public static string? TypeHint(this VBAParser.IdentifierContext? context)
        => context?.typedIdentifier()?.typeHint()?.GetText();
}
