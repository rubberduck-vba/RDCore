using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// A parameter of a <see cref="SignatureDescription"/>: where it is written in the signature's label, and what it is.
/// </summary>
/// <param name="Name">The name of the parameter.</param>
/// <param name="Start">The offset of the first character of the parameter in the label.</param>
/// <param name="End">The offset of the character after the parameter in the label.</param>
/// <param name="IsParamArray">Whether it is a <c>ParamArray</c>, which takes every argument from where it is on.</param>
/// <param name="Documentation">What the parameter is, in a sentence: its doc string; <see langword="null"/> when it has none.</param>
public sealed record class SignatureParameter(string Name, int Start, int End, bool IsParamArray = false, string? Documentation = null);

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
public sealed record class SignatureDescription(string Label, IReadOnlyList<SignatureParameter> Parameters, string? Documentation = null)
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
            parameters.Add(new SignatureParameter(parameter.Name, start, label.Length, parameter.IsParamArray, parameter.Documentation));
        }

        label.Append(')');
        var type = symbol is ITypedSymbol { ResolvedType: { } resolved } && resolved is not VBUnknownType ? resolved : null;
        if (keyword != "Sub" && type is not null && type.Name != VBTypeNames.VBVoid)
        {
            label.Append($" As {type.Name}");
        }

        return new SignatureDescription(label.ToString(), parameters, symbol.GetProperty(SymbolProperties.DocString));
    }

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
    private static IEnumerable<(string Name, string Text, bool IsParamArray, string? Documentation)> ParametersOf(Symbol symbol)
    {
        static string Modifiers(bool isOptional, ParameterKind kind)
            => $"{(isOptional ? "Optional " : string.Empty)}{(kind == ParameterKind.ExplicitByVal ? "ByVal " : string.Empty)}";

        static bool IsMe(string name, ParameterKind kind) => name == Tokens.Me && kind == ParameterKind.ImplicitByRef;

        IEnumerable<(string, string, bool, string?)> Bound(IEnumerable<VBParameterSymbol> parameters)
            => parameters.Where(parameter => !IsMe(parameter.Name, parameter.ParameterKind)).Select(parameter => parameter is ParamArrayParameterSymbol
                ? (parameter.Name, $"ParamArray {parameter.Name}() As Variant", true, parameter.GetProperty(SymbolProperties.DocString))
                : (parameter.Name, $"{Modifiers(parameter.IsOptional, parameter.ParameterKind)}{parameter.Name} As {parameter.ResolvedType.Name}", false,
                    parameter.GetProperty(SymbolProperties.DocString)));

        IEnumerable<(string, string, bool, string?)> Unbound(IEnumerable<UnboundVBParameterSymbol> parameters)
            => parameters.Where(parameter => !IsMe(parameter.Name, parameter.ParameterKind)).Select(parameter =>
                (parameter.Name, $"{Modifiers(parameter.IsOptional, parameter.ParameterKind)}{parameter.Name} As {parameter.ResolvedType.Name}", false,
                    parameter.GetProperty(SymbolProperties.DocString)));

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
