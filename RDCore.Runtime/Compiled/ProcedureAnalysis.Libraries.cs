using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Immutable;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call of a function of the standard library inside an expression: a conversion, or a function of the Math module, called as
// the interpreter calls it (CompiledActivation.CallLibrary).
internal sealed partial class ProcedureAnalysis
{
    /// <summary>
    /// The functions of the standard library the body calls inside an expression, by the number its nodes know each by.
    /// </summary>
    public List<CompiledLibraryCall> Libraries { get; } = [];

    // the functions compiled code calls, each of parameters of the Variant type passed by value, and whether each keeps anything from call to call: none
    // does but Rnd, whose seed moves at each call, so a call of it is made once in an instruction, and never again by the interpreter.
    private static readonly ImmutableDictionary<string, bool> CompiledLibraryFunctions = new Dictionary<string, bool>
    {
        ["IStdConversionModule.CBool/1"] = false,
        ["IStdConversionModule.CByte/1"] = false,
        ["IStdConversionModule.CCur/1"] = false,
        ["IStdConversionModule.CDate/1"] = false,
        ["IStdConversionModule.CDbl/1"] = false,
        ["IStdConversionModule.CInt/1"] = false,
        ["IStdConversionModule.CLng/1"] = false,
        ["IStdConversionModule.CLngLng/1"] = false,
        ["IStdConversionModule.CSng/1"] = false,
        ["IStdConversionModule.Fix/1"] = false,
        ["IStdConversionModule.Int/1"] = false,
        ["IStdMathModule.Abs/1"] = false,
        ["IStdMathModule.Rnd/1"] = true,
    }.ToImmutableDictionary(StringComparer.Ordinal);

    // whether symbol is a function of the standard library compiled code calls.
    private static bool IsCompiledLibrary(Symbol symbol)
        => symbol is VBFunctionMemberSymbol && symbol.GetProperty(SymbolProperties.ExternalTarget) is { } target && CompiledLibraryFunctions.ContainsKey(target)
            && StandardLibrary.IsLibraryMember(symbol);

    // a call of a function of the standard library (IsCompiledLibrary), written with its arguments, or by its name when it is given none, as the
    // interpreter makes it (InvokeProcedureFrom): each argument evaluated on payloads and held in a Variant of its own subtype, the parameters it gives no
    // argument given what an omitted one is. What it returns is a scalar of its type, or of the type of its argument for a function typed by it
    // (ArgumentTypedFunctions, the call's ResultType). Inside an instruction that calls inside an expression, the call is known once it is made, as any is;
    // in any other, it is of a function that keeps nothing, which the interpreter makes again when the instruction is handed to it.
    private PayloadNode? LibraryCallOf(BoundNode node, VBTypeMemberSymbol function, ImmutableArray<BoundArgument>? bound, VBType? resultType)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(function);
        var syntax = _nesting is null ? -1 : KnownOperands.IndexOf(_nesting, node.Expression);
        var keeps = CompiledLibraryFunctions[function.GetProperty(SymbolProperties.ExternalTarget)!];
        if (bound is not { } arguments || ScalarKinds.Of(resultType ?? ReturnedType(function)) is not { } kind || _nesting is not null && syntax < 0 || keeps && syntax < 0)
        {
            return Unsupported(node);
        }

        // an argument left out at the end is no argument: Rnd() is Rnd.
        var given = arguments.Length;
        while (given > 0 && arguments[given - 1].Passing is ArgumentPassing.Omitted)
        {
            given--;
        }

        if (given > 3 || given > parameters.Length)
        {
            return Unsupported(node);
        }

        var values = new List<VariantNode>(given);
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameter is ParamArrayParameterSymbol || parameter.ResolvedType is not VBVariantType || RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind)
                || parameter.GetProperty(SymbolProperties.PassedByAddress) || i >= given && !parameter.IsOptional)
            {
                return Unsupported(node);
            }

            if (i >= given)
            {
                continue;
            }

            if (arguments[i] is not { Passing: ArgumentPassing.Value, Value: { } value } || Dynamic(value) is not { } argument)
            {
                return Unsupported(node);
            }

            values.Add(argument);
        }

        Libraries.Add(new CompiledLibraryCall(function, parameters, node.Expression.SourceLocation));
        return new LibraryCallNode(Libraries.Count - 1, values, kind, syntax);
    }
}
