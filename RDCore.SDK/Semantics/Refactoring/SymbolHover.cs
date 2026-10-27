using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using System.Globalization;
using System.Resources;

namespace RDCore.SDK.Semantics.Refactoring;

/// <summary>
/// What a hover over a name says of its symbol (<strong>LSP 3.17</strong> §Hover Request): its declaration as code would write it, what declares it, and what
/// it does when its doc string says so.
/// </summary>
/// <param name="Declaration">The declaration, on one line: <c>Public Function GetTotal() As Long</c>, <c>Dim count As Long</c>, <c>Enum Colour</c>.</param>
/// <param name="Container">What declares the symbol: its module, procedure, <c>Enum</c> or <c>Type</c> - <c>Helpers</c>, <c>VBA.Strings</c>; <see langword="null"/>
/// for a module, which nothing declares.</param>
/// <param name="Documentation">What the symbol is or does, in a sentence: its doc string; <see langword="null"/> when it has none.</param>
public sealed record class SymbolHover(string Declaration, string? Container, string? Documentation)
{
    private static readonly ResourceManager Messages = new("RDCore.SDK.Semantics.Refactoring.RefactoringMessages", typeof(SymbolHover).Assembly);

    /// <summary>
    /// What a hover says of a symbol.
    /// </summary>
    /// <param name="symbol">The symbol.</param>
    public static SymbolHover Of(Symbol symbol)
    {
        var path = NameOccurrences.Describe(symbol);
        var dot = path.LastIndexOf('.');
        var container = symbol is VBModuleSymbol || dot <= 0 ? null : path[..dot];

        // a member of a library is a member of its module in that library: VBA.Interaction.
        if (container is not null && symbol.GetProperty(SymbolProperties.Library) is { Length: > 0 } library)
        {
            container = $"{library}.{container}";
        }
        return new SymbolHover(DeclarationOf(symbol), container, symbol.GetProperty(SymbolProperties.DocString));
    }

    /// <summary>
    /// The hover as Markdown: the declaration as a block of VBA, what declares it, and its doc string.
    /// </summary>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public string ToMarkdown(CultureInfo? culture = null)
        => string.Join("\n\n", new[] { $"```vba\n{Declaration}\n```", MemberOf(culture), Documentation }.Where(part => part is { Length: > 0 }));

    /// <summary>
    /// The hover as plain text, for a client that does not read Markdown.
    /// </summary>
    /// <param name="culture">The language of the reader; the current UI culture by default.</param>
    public string ToPlainText(CultureInfo? culture = null)
        => string.Join("\n", new[] { Declaration, MemberOf(culture), Documentation }.Where(part => part is { Length: > 0 }));

    private string? MemberOf(CultureInfo? culture)
    {
        if (Container is null)
        {
            return null;
        }

        culture ??= CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString("Hover_MemberOf", culture) ?? "{0}", Container);
    }

    // the declaration as code writes it: what is called by its signature, what holds a value by its type.
    private static string DeclarationOf(Symbol symbol)
    {
        var access = symbol is AccessibleTypedSymbol { AccessModifier: not AccessModifier.Implicit and var modifier } ? $"{modifier} " : string.Empty;
        return symbol switch
        {
            VBModuleSymbol module => $"{(module.Kind == SymbolKindExt.Class ? "Class" : "Module")} {module.Name}",
            VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol when SignatureDescription.Of(symbol) is { } property
                => access + property.Label.Replace("Property ", $"Property {AccessorOf(symbol)} ", StringComparison.Ordinal),
            _ when SignatureDescription.Of(symbol) is { } signature => access + signature.Label,
            VBEventMemberSymbol => $"{access}Event {symbol.Name}",
            VBEnumMemberSymbol => $"{access}Enum {symbol.Name}",
            VBEnumConstMemberSymbol => symbol.Name,
            VBUserDefinedTypeMemberSymbol => $"{access}Type {symbol.Name}",
            ParamArrayParameterSymbol => $"ParamArray {symbol.Name}() As Variant",
            VBParameterSymbol parameter => $"{(parameter.IsOptional ? "Optional " : string.Empty)}{PassingOf(parameter.ParameterKind)}{symbol.Name}{AsTypeOf(symbol)}",
            VBLocalConstantSymbol => $"Const {symbol.Name}{AsTypeOf(symbol)}",
            VBConstantMemberSymbol => $"{access}Const {symbol.Name}{AsTypeOf(symbol)}",
            VBLocalVariableSymbol local => $"{(local.IsStatic ? "Static" : "Dim")} {symbol.Name}{AsTypeOf(symbol)}",
            VBUserDefinedTypeFieldSymbol => $"{symbol.Name}{AsTypeOf(symbol)}",
            _ => $"{(access.Length > 0 ? access : "Dim ")}{symbol.Name}{AsTypeOf(symbol)}",
        };
    }

    private static string AccessorOf(Symbol property) => property switch
    {
        VBPropertyLetMemberSymbol => "Let",
        VBPropertySetMemberSymbol => "Set",
        _ => "Get",
    };

    private static string PassingOf(ParameterKind kind) => kind switch
    {
        ParameterKind.ExplicitByVal => "ByVal ",
        ParameterKind.ExplicitByRef => "ByRef ",
        _ => string.Empty,
    };

    // the type a value is declared as; nothing when it is not known.
    private static string AsTypeOf(Symbol symbol)
        => symbol is ITypedSymbol { ResolvedType: { } type } && type is not VBUnknownType ? $" As {type.Name}" : string.Empty;
}
