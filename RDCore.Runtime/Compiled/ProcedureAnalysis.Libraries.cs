using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
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

    // the functions compiled code calls, each of parameters of the Variant type or of a scalar type passed by value, and whether each keeps anything from
    // call to call: none does but Rnd, whose seed moves at each call, so a call of it is made once in an instruction, and never again by the interpreter.
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
        ["IStdInformationModule.IsEmpty/1"] = false,
        ["IStdInformationModule.IsNull/1"] = false,
        ["IStdInformationModule.IsNumeric/1"] = false,
        ["IStdInformationModule.IsObject/1"] = false,
        ["IStdInformationModule.VarType/1"] = false,
        ["IStdMathModule.Abs/1"] = false,
        ["IStdMathModule.Atn/1"] = false,
        ["IStdMathModule.Cos/1"] = false,
        ["IStdMathModule.Exp/1"] = false,
        ["IStdMathModule.Log/1"] = false,
        ["IStdMathModule.Rnd/1"] = true,
        ["IStdMathModule.Sin/1"] = false,
        ["IStdMathModule.Sqr/1"] = false,
        ["IStdMathModule.Tan/1"] = false,
    }.ToImmutableDictionary(StringComparer.Ordinal);

    // the functions compiled code calls for a Variant they return, each of parameters of the Variant type or of a scalar type passed by value, and keeping
    // nothing: IIf, which is given both its parts, evaluated, the true part first (MS-VBAL §6.1.2.8.1.11); Round and Sgn; and the dates made of their
    // parts, and the parts of a date.
    private static readonly ImmutableHashSet<string> VariantLibraryFunctions = ImmutableHashSet.Create(StringComparer.Ordinal,
        "IStdInteractionModule.IIf/3",
        "IStdMathModule.Round/2",
        "IStdMathModule.Sgn/1",
        "IStdDateTimeModule.DateSerial/3",
        "IStdDateTimeModule.TimeSerial/3",
        "IStdDateTimeModule.Year/1",
        "IStdDateTimeModule.Month/1",
        "IStdDateTimeModule.Day/1",
        "IStdDateTimeModule.Hour/1",
        "IStdDateTimeModule.Minute/1",
        "IStdDateTimeModule.Second/1");

    // whether symbol is a function of the standard library compiled code calls for the Variant it returns.
    private static bool IsVariantLibrary(Symbol symbol)
        => symbol is VBFunctionMemberSymbol && symbol.GetProperty(SymbolProperties.ExternalTarget) is { } target && VariantLibraryFunctions.Contains(target)
            && StandardLibrary.IsLibraryMember(symbol);

    // a call of such a function, with its arguments as LibraryCallOf takes them (LibraryArgumentsOf).
    private VariantNode? LibraryVariantOf(BoundCall call, VBTypeMemberSymbol function)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(function);
        if (call.BoundArguments is not { } arguments || LibraryArgumentsOf(parameters, arguments) is not { } values)
        {
            Unsupported(call);
            return null;
        }

        Libraries.Add(new CompiledLibraryCall(function, parameters, call.Expression.SourceLocation));
        return new LibraryVariantNode(Libraries.Count - 1, values);
    }

    // the arguments of a call of a function of the standard library as the interpreter gives them (InvokeProcedureFrom), each passed by value: evaluated on
    // payloads and held in a Variant of its own subtype, or let-coerced to the type of a parameter of a scalar type (MS-VBAL §5.3.1.11). An argument left
    // out at the end is no argument (Rnd() is Rnd), and its parameter, which is optional, is given what an omitted one is. None when compiled code cannot
    // give them.
    private List<VariantNode>? LibraryArgumentsOf(ImmutableArray<VBParameterSymbol> parameters, ImmutableArray<BoundArgument> arguments)
    {
        var given = arguments.Length;
        while (given > 0 && arguments[given - 1].Passing is ArgumentPassing.Omitted)
        {
            given--;
        }

        if (given > 3 || given > parameters.Length)
        {
            return null;
        }

        var values = new List<VariantNode>(given);
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var typed = parameter.ResolvedType is VBEnumType ? null : ScalarKinds.Of(parameter.ResolvedType);
            if (parameter is ParamArrayParameterSymbol || parameter.ResolvedType is not VBVariantType && typed is null
                || RuntimeProcedureInvoker.IsByRef(parameter.ParameterKind) || parameter.GetProperty(SymbolProperties.PassedByAddress)
                || i >= given && !parameter.IsOptional)
            {
                return null;
            }

            if (i >= given)
            {
                continue;
            }

            // a Variant variable given to a Variant parameter is given what it holds, whatever it holds (VariantValueNode).
            if (arguments[i] is not { Passing: ArgumentPassing.Value, Value: { } value }
                || (typed is { } destination ? PayloadAs(value, destination) is { } coerced ? new VariantOfNode(coerced) : null
                    : value is BoundVariable variable && VariantSlotOf(variable) is { } slot ? new VariantValueNode(slot) : Dynamic(value)) is not { } argument)
            {
                return null;
            }

            values.Add(argument);
        }

        return values;
    }

    // whether symbol is a function of the standard library compiled code calls.
    private static bool IsCompiledLibrary(Symbol symbol)
        => symbol is VBFunctionMemberSymbol && symbol.GetProperty(SymbolProperties.ExternalTarget) is { } target && CompiledLibraryFunctions.ContainsKey(target)
            && StandardLibrary.IsLibraryMember(symbol);

    // a call of a function of the standard library (IsCompiledLibrary), written with its arguments, or by its name when it is given none, as the
    // interpreter makes it (LibraryArgumentsOf). What it returns is a scalar of its type, or of the type of its argument for a function typed by it
    // (ArgumentTypedFunctions, the call's ResultType). Inside an instruction that calls inside an expression, the call is known once it is made, as any is;
    // in any other, it is of a function that keeps nothing, which the interpreter makes again when the instruction is handed to it.
    private PayloadNode? LibraryCallOf(BoundNode node, VBTypeMemberSymbol function, ImmutableArray<BoundArgument>? bound, VBType? resultType)
    {
        var parameters = RuntimeProcedureInvoker.GetParameters(function);
        var syntax = _nesting is null ? -1 : KnownOperands.IndexOf(_nesting, node.Expression);
        var keeps = CompiledLibraryFunctions[function.GetProperty(SymbolProperties.ExternalTarget)!];
        if (bound is not { } arguments || ScalarKinds.Of(resultType ?? ReturnedType(function)) is not { } kind || _nesting is not null && syntax < 0 || keeps && syntax < 0
            || LibraryArgumentsOf(parameters, arguments) is not { } values)
        {
            return Unsupported(node);
        }

        Libraries.Add(new CompiledLibraryCall(function, parameters, node.Expression.SourceLocation));
        return new LibraryCallNode(Libraries.Count - 1, values, kind, syntax);
    }
}
