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

namespace RDCore.LanguageServer.Services.Refactoring;

public sealed record class SymbolHover(string Declaration, string? Container, string? Documentation)
{
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
        return new SymbolHover(DeclarationOf(symbol), container, symbol.GetProperty(SymbolProperties.DocString))
        {
            ControlArrayOf = element is null ? null : ClassNameOf(element),
            AdditionTo = symbol.GetProperty(SymbolProperties.LibraryAddition) ? symbol.GetProperty(SymbolProperties.Library) : null,
            IsDefaultInstance = symbol is VBPredeclaredInstanceSymbol,
        };
    }

    public string? ControlArrayOf { get; init; }

    // the default instance variable of a class (MS-VBAL §5.2.4.1.2), which no statement declares: it is said by its name, not by a declaration.
    public bool IsDefaultInstance { get; init; }

    // the library RD-VBA adds the member to, which the library's type library does not declare.
    public string? AdditionTo { get; init; }

    public string ToMarkdown(CultureInfo? culture = null)
    {
        var declaration = IsDefaultInstance ? SymbolTexts.DefaultInstance($"`{Declaration}`", culture) : $"```vba\n{Declaration}\n```";
        return string.Join("\n\n", PartsOf(declaration, culture));
    }

    public string ToPlainText(CultureInfo? culture = null)
        => string.Join("\n", PartsOf(IsDefaultInstance ? SymbolTexts.DefaultInstance(Declaration, culture) : Declaration, culture));

    // what the hover says, in order: the declaration, what declares the symbol, what a control array is of, what adds it to its library, and its doc string.
    private IEnumerable<string?> PartsOf(string declaration, CultureInfo? culture)
        => new[] { declaration, MemberOf(culture), ArrayOf(culture), AdditionOf(culture), Documentation }.Where(part => part is { Length: > 0 });

    private string? AdditionOf(CultureInfo? culture) => AdditionTo is null ? null : SymbolTexts.LibraryAddition(AdditionTo, culture);

    private string? ArrayOf(CultureInfo? culture) => ControlArrayOf is null ? null : SymbolTexts.ControlArrayOf(ControlArrayOf, culture);

    // a class of a type library by its library's name and its own, as a declaration names it: VB.CommandButton.
    private static string ClassNameOf(RDCore.SDK.Model.Types.Abstract.VBType type)
        => type is RDCore.SDK.Model.Types.Complex.VBClassType { Symbol: var symbol } && symbol.GetProperty(SymbolProperties.Library) is { Length: > 0 } library
            ? $"{library}.{type.Name}"
            : type.Name;

    private string? MemberOf(CultureInfo? culture) => Container is null ? null : SymbolTexts.MemberOf(Container, culture);

    // the declaration as code writes it: what is called by its signature, what holds a value by its type. An intrinsic type (MS-VBAL §2.1), which nothing
    // declares, is its name: Long.
    private static string DeclarationOf(Symbol symbol)
    {
        var access = symbol is AccessibleTypedSymbol { AccessModifier: not AccessModifier.Implicit and var modifier } ? $"{modifier} " : string.Empty;
        return symbol switch
        {
            StaticSymbol { Kind: SymbolKindExt.TypeDescriptor, ResolvedType: var intrinsic } => intrinsic.Name,
            VBModuleSymbol module => $"{(module.Kind == SymbolKindExt.Class ? "Class" : "Module")} {module.Name}",
            VBPredeclaredInstanceSymbol => symbol.Name,
            VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol when SignatureDescription.Of(symbol) is { } property
                => access + ProcedureKeywords.Of(symbol) + property.Label[Tokens.Property.Length..],
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
