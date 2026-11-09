using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.Completion;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Workspace.Forms;
using System.Globalization;
using System.Resources;
using RDCore.SDK.Semantics.Refactoring;

namespace RDCore.LanguageServer.Services.Refactoring;

public sealed record class SymbolHover(string Declaration, string? Container, string? Documentation)
{
    private static readonly ResourceManager Messages = new("RDCore.SDK.Semantics.Refactoring.RefactoringMessages", typeof(RefactoringTitles).Assembly);

    public static SymbolHover Of(Symbol symbol)
    {
        var path = NameOccurrences.Describe(symbol);
        var dot = path.LastIndexOf('.');
        var container = symbol is VBModuleSymbol || dot <= 0 ? null : path[..dot];

        // a member of a library is a member of its module in that library: VBA.Interaction. A type library's classes are addressed under its name already
        // (Excel.Worksheet), the standard library's modules are not (Interaction).
        if (container is not null && symbol.GetProperty(SymbolProperties.Library) is { Length: > 0 } library
            && SymbolUri.ModuleNameOf(symbol.Uri) is var first && !string.Equals(first, library, StringComparison.OrdinalIgnoreCase))
        {
            container = $"{library}.{container}";
        }

        // the class of a control array has a name no code writes, and is not said: its members are reached through the array, which says what it is.
        if (symbol.GetProperty(SymbolProperties.Library) == FormControlArray.LibraryName)
        {
            container = null;
        }

        var element = symbol is ITypedSymbol { ResolvedType: var type } ? FormControlArray.ElementOf(type) : null;
        return new SymbolHover(DeclarationOf(symbol), container, symbol.GetProperty(SymbolProperties.DocString)) { ControlArrayOf = element is null ? null : ClassNameOf(element) };
    }

    public string? ControlArrayOf { get; init; }

    public string ToMarkdown(CultureInfo? culture = null)
        => string.Join("\n\n", new[] { $"```vba\n{Declaration}\n```", MemberOf(culture), ArrayOf(culture), Documentation }.Where(part => part is { Length: > 0 }));

    public string ToPlainText(CultureInfo? culture = null)
        => string.Join("\n", new[] { Declaration, MemberOf(culture), ArrayOf(culture), Documentation }.Where(part => part is { Length: > 0 }));

    private string? ArrayOf(CultureInfo? culture)
    {
        if (ControlArrayOf is null)
        {
            return null;
        }

        culture ??= CultureInfo.CurrentUICulture;
        return string.Format(culture, Messages.GetString("Hover_ControlArrayOf", culture) ?? "{0}", ControlArrayOf);
    }

    // a class of a type library by its library's name and its own, as a declaration names it: VB.CommandButton.
    private static string ClassNameOf(RDCore.SDK.Model.Types.Abstract.VBType type)
        => type is RDCore.SDK.Model.Types.Complex.VBClassType { Symbol: var symbol } && symbol.GetProperty(SymbolProperties.Library) is { Length: > 0 } library
            ? $"{library}.{type.Name}"
            : type.Name;

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
