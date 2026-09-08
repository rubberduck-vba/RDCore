using Antlr4.Runtime.Tree;
using System.Text;

namespace RDCore.Parsing.Syntax;

/// <summary>
/// Null-tolerant identifier-name extraction from the VBA grammar's identifier rules
/// (<c>identifier → (typed|untyped)Identifier → identifierValue → IDENTIFIER | keyword | foreignName</c>).
/// The name behind an identifier can be a plain <c>IDENTIFIER</c>, a keyword (<c>Dim Name As String</c>),
/// or a bracketed foreign name (<c>Dim [My Var]</c> — the brackets are escape syntax, not part of the
/// name). Under LL error recovery any intermediate rule context can be <c>null</c>, so every accessor
/// short-circuits to <see cref="string.Empty"/> rather than throwing on a half-typed declaration.
/// </summary>
/// <remarks>Shape inspired by Rubberduck's <c>Identifier.GetName</c> (GPLv3, same lineage).</remarks>
internal static class IdentifierNameExtensions
{
    /// <summary>The identifier value, brackets stripped and type-hint excluded; empty when unresolvable.</summary>
    public static string Name(this VBAParser.IdentifierValueContext? context)
    {
        if (context is null)
        {
            return string.Empty;
        }
        if (context.foreignName() is { } foreign)
        {
            // '[' foreignIdentifier* ']' — join the inner text, dropping the brackets.
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

    /// <summary>
    /// <c>true</c> when <paramref name="text"/> is an ANTLR error-recovery placeholder — the display
    /// form <c>&lt;missing X&gt;</c> that <c>DefaultErrorStrategy</c> gives a synthetically inserted
    /// token. Such text is never real source and must not land in the AST as a name or a value.
    /// </summary>
    internal static bool IsRecoveryPlaceholder(string? text)
        => text is null || text.StartsWith("<missing ", StringComparison.Ordinal);

    /// <summary>
    /// The terminal's source text, or <c>null</c> when it is a synthetic token ANTLR inserted during
    /// error recovery (a negative token index, or a <c>&lt;missing X&gt;</c> body).
    /// </summary>
    internal static string? RealText(this ITerminalNode? node)
        => node is { Symbol.TokenIndex: >= 0 } && !IsRecoveryPlaceholder(node.GetText())
            ? node.GetText()
            : null;

    /// <summary>The identifier text, without any type-declaration character; empty when unresolvable.</summary>
    public static string Name(this VBAParser.IdentifierContext? context)
        => (context?.untypedIdentifier() ?? context?.typedIdentifier()?.untypedIdentifier())?.identifierValue().Name()
           ?? string.Empty;

    /// <summary>The identifier text; empty when unresolvable.</summary>
    public static string Name(this VBAParser.UntypedIdentifierContext? context)
        => context?.identifierValue().Name() ?? string.Empty;

    /// <summary>
    /// The name behind an <c>unrestrictedIdentifier</c> — an <c>identifier</c> or a reserved-word
    /// form; falls back to the raw text for the reserved-word form.
    /// </summary>
    public static string Name(this VBAParser.UnrestrictedIdentifierContext? context)
        => context is null
            ? string.Empty
            : context.identifier().Name() is { Length: > 0 } name ? name : context.GetText();

    /// <summary>The subroutine name (<c>Sub</c> / <c>Property Let</c> / <c>Property Set</c>); empty when unresolvable.</summary>
    public static string Name(this VBAParser.SubroutineNameContext? context)
        => context?.identifier().Name() ?? string.Empty;

    /// <summary>The function name (<c>Function</c> / <c>Property Get</c>); empty when unresolvable.</summary>
    public static string Name(this VBAParser.FunctionNameContext? context)
        => context?.identifier().Name() ?? string.Empty;

    /// <summary>The name of a user-defined-type field (either the reserved-word or untyped form); empty when unresolvable.</summary>
    public static string Name(this VBAParser.UdtMemberContext? context)
        => context?.reservedNameMemberDeclaration()?.unrestrictedIdentifier().Name() is { Length: > 0 } reserved
            ? reserved
            : context?.untypedNameMemberDeclaration()?.untypedIdentifier().Name() ?? string.Empty;

    /// <summary>The type-declaration character on a typed identifier (<c>%</c>, <c>&amp;</c>, …), or <c>null</c>.</summary>
    public static string? TypeHint(this VBAParser.IdentifierContext? context)
        => context?.typedIdentifier()?.typeHint()?.GetText();
}
