using System.Text.RegularExpressions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Static;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// A parameter of a <see cref="SignatureDescription"/>: where it is written in the signature's label, and what it is.
/// </summary>
/// <param name="Name">The name of the parameter.</param>
/// <param name="Start">The offset of the first character of the parameter in the label.</param>
/// <param name="End">The offset of the character after the parameter in the label.</param>
/// <param name="IsParamArray">Whether it is a <c>ParamArray</c>, which takes every argument from where it is on.</param>
/// <param name="Documentation">What the parameter is, in a sentence: its doc string; <see langword="null"/> when it has none.</param>
/// <param name="Type">
/// The declared type of the parameter: an argument given to it is let-coerced to it. An <c>Enum</c>'s, for one whose arguments are its constants (<c>Buttons As
/// VbMsgBoxStyle</c>), which a completion list offers first; <see langword="null"/> for a <c>ParamArray</c>, whose arguments are of any type.
/// </param>
/// <param name="IsOptional">Whether it is declared <c>Optional</c>: a call need not give it an argument (<strong>MS-VBAL §5.3.1.11</strong>).</param>
public sealed record class SignatureParameter(string Name, int Start, int End, bool IsParamArray = false, string? Documentation = null, VBType? Type = null, bool IsOptional = false)
{
    /// <summary>
    /// The parameter as the signature's label writes it: <c>Optional ByVal Length As Long</c>.
    /// </summary>
    /// <param name="signature">The signature the parameter is of.</param>
    public string TextIn(SignatureDescription signature) => signature.Label[Start..End];
}

/// <summary>
/// What a procedure takes, as a signature help shows it (<strong>LSP 3.17</strong> §Signature Help): its signature on one line - <c>Function Left(String As
/// Variant, Length As Long) As Variant</c> - each of its parameters where the line has it, and what the procedure does.
/// </summary>
/// <remarks>
/// The implicit <c>Me</c> of an instance member is none of its parameters: it is the object the member is called on, and no argument is written for it.
/// </remarks>
/// <param name="Label">The signature, on one line.</param>
/// <param name="Parameters">The parameters, in the order they are declared.</param>
/// <param name="Documentation">What the procedure does, in a sentence: its doc string (<see cref="SymbolProperties.DocString"/>); <see langword="null"/> when it has none.</param>
public sealed partial record class SignatureDescription(string Label, IReadOnlyList<SignatureParameter> Parameters, string? Documentation = null)
{
    /// <summary>
    /// The signature of a procedure, a function or a property; <see langword="null"/> for a symbol that is not called.
    /// </summary>
    /// <param name="symbol">The symbol.</param>
    public static SignatureDescription? Of(Symbol symbol)
    {
        var keyword = symbol.Kind switch
        {
            SymbolKindExt.Procedure => "Sub",
            SymbolKindExt.Function => "Function",
            SymbolKindExt.Property => "Property",
            _ => null,
        };

        if (keyword is null)
        {
            return null;
        }

        var label = new System.Text.StringBuilder($"{keyword} {symbol.Name}(");
        var parameters = new List<SignatureParameter>();
        foreach (var parameter in ParametersOf(symbol))
        {
            if (parameters.Count > 0)
            {
                label.Append(", ");
            }

            var start = label.Length;
            label.Append(parameter.Text);
            parameters.Add(new SignatureParameter(parameter.Name, start, label.Length, parameter.IsParamArray, parameter.Documentation, parameter.Type, parameter.IsOptional));
        }

        label.Append(')');
        var type = symbol is ITypedSymbol { ResolvedType: { } resolved } && resolved is not VBUnknownType ? resolved : null;
        if (keyword != "Sub" && type is not null && type.Name != VBTypeNames.VBVoid)
        {
            label.Append($" As {type.Name}");
        }

        return new SignatureDescription(label.ToString(), parameters, symbol.GetProperty(SymbolProperties.DocString))
        {
            NamesArePlaceholders = StdLibSymbolProvider.IsLibraryMember(symbol) && parameters.Any(parameter => PlaceholderName().IsMatch(parameter.Name)),
        };
    }

    /// <summary>
    /// Whether the names of the parameters stand in for names the procedure does not have: the specification names the parameters of a few functions of the
    /// standard library <c>Arg1</c>, <c>Arg2</c> and so on (<c>InStr</c>, <c>InStrB</c>, <strong>MS-VBAL §6.1.2.11.1.14</strong>), and MS-VBA takes none of
    /// them as the name of a named argument.
    /// </summary>
    public bool NamesArePlaceholders { get; init; }

    /// <summary>
    /// The signature of what a call written as a name and an argument list calls (<c>p(1, 2)</c>, <c>o.Items(1)</c>, <c>Me(1)</c>), <paramref name="called"/>
    /// being what the name binds to; <see langword="null"/> when it is nothing a call can be made of.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A procedure that takes arguments is what is called, and so is a <c>Sub</c>. An argument list after anything else (a variable, a constant, a function or
    /// a <c>Property Get</c> that takes no arguments) is given to what that is: to the default member of its class, for an object (<strong>MS-VBAL
    /// §5.6.13</strong>). When that default member takes no arguments, the default member of the class it returns is followed in turn, until a member takes
    /// arguments: the signature is that member's, under its own name.
    /// </para>
    /// <para>
    /// An array is indexed, and a value of any other type, or an object of a class with no default member, takes no argument list: neither has a signature.
    /// A function that takes no arguments and returns no object with a default member has its own.
    /// </para>
    /// </remarks>
    /// <param name="called">What the name of the call binds to; <see langword="null"/> when it binds to nothing.</param>
    /// <param name="resolver">
    /// The resolver names are bound by, which a class of the workspace is read from as the composition has it now (<see cref="CurrentTypes"/>);
    /// <see langword="null"/> when there is none.
    /// </param>
    public static SignatureDescription? OfCall(Symbol? called, ISymbolResolver? resolver)
    {
        var symbol = called;
        for (var depth = 0; symbol is not null && depth < MaxDefaultMembers; depth++)
        {
            var signature = Of(symbol);
            if (signature is not null && (signature.Parameters.Count > 0 || symbol.Kind == SymbolKindExt.Procedure))
            {
                return signature;
            }

            if (ClassOf(symbol, resolver) is not { } declared || CurrentTypes.Of(resolver, declared) is not { DefaultMember: { } member })
            {
                // nothing to give the arguments to: a function that takes none is described as it is, and anything else has no signature.
                return signature;
            }

            symbol = member;
        }

        return null;
    }

    // the class of what a symbol is, or returns: Me is an object of the class its procedure is a member of, which the parameter is not declared as
    // (MS-VBAL §5.6.11).
    private static VBClassType? ClassOf(Symbol symbol, ISymbolResolver? resolver) => symbol switch
    {
        VBParameterSymbol { Name: Tokens.Me, ParameterKind: ParameterKind.ImplicitByRef } when resolver is not null
            => resolver.ResolveType(SymbolUri.ModuleNameOf(symbol.ParentUri), ScopeKind.Global, StaticSymbol.GlobalUri).Symbol is VBClassModuleSymbol module
                ? VBClassType.Of(module)
                : null,
        ITypedSymbol { ResolvedType: VBClassType declared } => declared,
        _ => null,
    };

    // a chain of default members longer than this is a class that is its own default member's class, whose arguments go nowhere.
    private const int MaxDefaultMembers = 8;

    [GeneratedRegex(@"^Arg\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderName();

    /// <summary>
    /// The parameter an argument is given to: the one it names, or the one at its position - the <c>ParamArray</c> for every argument from where it is on.
    /// </summary>
    /// <param name="argumentIndex">The position of the argument, from 0.</param>
    /// <param name="argumentName">The name a named argument is written with (<c>Prompt:=</c>); <see langword="null"/> for one that is not named.</param>
    /// <returns>The index of the parameter; <see langword="null"/> when the argument is given to none.</returns>
    public int? ActiveParameterOf(int argumentIndex, string? argumentName = null)
    {
        if (argumentName is { Length: > 0 })
        {
            var named = Parameters.ToList().FindIndex(parameter => string.Equals(parameter.Name, argumentName, StringComparison.OrdinalIgnoreCase));
            return named < 0 ? null : named;
        }

        if (argumentIndex < Parameters.Count)
        {
            return argumentIndex;
        }

        return Parameters.Count > 0 && Parameters[^1].IsParamArray ? Parameters.Count - 1 : null;
    }

    /// <summary>
    /// The signature as <strong>LSP</strong> has it: each parameter by where it is in the label.
    /// </summary>
    /// <param name="activeParameter">The parameter the argument at the cursor is given to (<see cref="ActiveParameterOf"/>).</param>
    public SignatureInformation ToSignatureInformation(int? activeParameter = null) => new()
    {
        Label = Label,
        Documentation = Documentation is { Length: > 0 } documentation ? new StringOrMarkupContent(documentation) : null,
        Parameters = new Container<ParameterInformation>(Parameters.Select(parameter => new ParameterInformation
        {
            Label = new ParameterInformationLabel((parameter.Start, parameter.End)),
            Documentation = parameter.Documentation is { Length: > 0 } documentation ? new StringOrMarkupContent(documentation) : null,
        })),
        ActiveParameter = activeParameter,
    };

    // the parameters of what is called, as they are written: `Optional ByVal Length As Long`, `ParamArray Values() As Variant`; the implicit Me is none of them.
    private static IEnumerable<(string Name, string Text, bool IsParamArray, string? Documentation, VBType? Type, bool IsOptional)> ParametersOf(Symbol symbol)
    {
        static string Modifiers(bool isOptional, ParameterKind kind)
            => $"{(isOptional ? "Optional " : string.Empty)}{(kind == ParameterKind.ExplicitByVal ? "ByVal " : string.Empty)}";

        static bool IsMe(string name, ParameterKind kind) => name == Tokens.Me && kind == ParameterKind.ImplicitByRef;

        IEnumerable<(string, string, bool, string?, VBType?, bool)> Bound(IEnumerable<VBParameterSymbol> parameters)
            => parameters.Where(parameter => !IsMe(parameter.Name, parameter.ParameterKind)).Select(parameter => parameter is ParamArrayParameterSymbol
                ? (parameter.Name, $"ParamArray {parameter.Name}() As Variant", true, parameter.GetProperty(SymbolProperties.DocString), (VBType?)null, false)
                : (parameter.Name, $"{Modifiers(parameter.IsOptional, parameter.ParameterKind)}{parameter.Name} As {parameter.ResolvedType.Name}", false,
                    parameter.GetProperty(SymbolProperties.DocString), parameter.ResolvedType, parameter.IsOptional));

        IEnumerable<(string, string, bool, string?, VBType?, bool)> Unbound(IEnumerable<UnboundVBParameterSymbol> parameters)
            => parameters.Where(parameter => !IsMe(parameter.Name, parameter.ParameterKind)).Select(parameter =>
                (parameter.Name, $"{Modifiers(parameter.IsOptional, parameter.ParameterKind)}{parameter.Name} As {parameter.ResolvedType.Name}", false,
                    parameter.GetProperty(SymbolProperties.DocString), (VBType?)parameter.ResolvedType, parameter.IsOptional));

        return symbol switch
        {
            VBReturningMemberSymbol member => Bound(member.Parameters),
            VBProcedureMemberSymbol member => Bound(member.Parameters),
            UnboundVBReturningMemberSymbol member => Unbound(member.Parameters),
            UnboundVBProcedureMemberSymbol member => Unbound(member.Parameters),
            _ => [],
        };
    }
}
