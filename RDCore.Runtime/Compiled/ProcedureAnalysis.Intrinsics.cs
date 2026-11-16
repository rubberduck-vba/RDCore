using RDCore.Runtime.Execution;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.LanguageCore.StdLib;
using RDCore.Runtime.LanguageCore.StdLib.Abstract;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.StdLib;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Immutable;
using System.Reflection;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call of a function of the standard library whose arguments it knows the types of: the typed form of the function
// (StdLibIntrinsicAttribute), called with no Variant and no frame; and a conversion of a whole number, which is the number let-coerced to the conversion's
// type (MS-VBAL §6.1.2.3.1), as compiled code let-coerces it.
internal sealed partial class ProcedureAnalysis
{
    // the typed forms of the library's functions, by the function each is of: the methods of the implementations of the library's modules that carry the
    // attribute.
    private static readonly ImmutableDictionary<string, ImmutableArray<MethodInfo>> Intrinsics = typeof(StdStrings).Assembly.GetTypes()
        .Where(type => type.Namespace == typeof(StdStrings).Namespace)
        .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        .Select(method => (Method: method, Intrinsic: method.GetCustomAttribute<StdLibIntrinsicAttribute>()))
        .Where(marked => marked.Intrinsic is not null)
        .GroupBy(
            marked => StdLibSymbolReader.ExternalTargetOf(marked.Intrinsic!.Module, marked.Intrinsic.Module.GetMethod(marked.Intrinsic.Member)!),
            marked => marked.Method)
        .ToImmutableDictionary(group => group.Key, group => group.ToImmutableArray(), StringComparer.Ordinal);

    // a call of a function of the library that has a typed form of the types of its arguments, outside an instruction that calls inside an expression: the
    // call of that form. None when the function has none of those types; nothing is refused then, and the call is made as any other.
    private IntrinsicNode? IntrinsicOf(BoundNode node)
    {
        if (_nesting is not null || node is not BoundCall { Procedure: var function, BoundArguments: { } arguments }
            || !StandardLibrary.IsLibraryMember(function) || function.GetProperty(SymbolProperties.ExternalTarget) is not { } target
            || !Intrinsics.TryGetValue(target, out var methods))
        {
            return null;
        }

        foreach (var method in methods)
        {
            if (ResultKindOf(method) is { } kind && IntrinsicArgumentsOf(method, arguments) is { } values)
            {
                return new IntrinsicNode(method, values, kind);
            }
        }

        return null;
    }

    // what a typed form returns, of a payload's kind: an int is a Long, a double a Double, whether it is returned or given back in an out parameter.
    private static ScalarKind? ResultKindOf(MethodInfo method)
    {
        var parameters = method.GetParameters();
        var returned = method.ReturnType == typeof(bool) && parameters is [.., { IsOut: true } result]
            ? result.ParameterType.GetElementType()
            : method.ReturnType;
        return returned == typeof(int) ? ScalarKind.Long : returned == typeof(double) ? ScalarKind.Double : null;
    }

    // the arguments of a call given to a typed form, when each is of the type of its parameter: a String's text for a string; a whole number for a long; a
    // Double for a double. Each argument is given, by value: the form has no optional parameter.
    private List<PayloadNode>? IntrinsicArgumentsOf(MethodInfo method, ImmutableArray<BoundArgument> arguments)
    {
        var parameters = method.GetParameters().Where(parameter => !parameter.IsOut).ToArray();
        if (arguments.Length != parameters.Length)
        {
            return null;
        }

        var values = new List<PayloadNode>(parameters.Length);
        for (var i = 0; i < parameters.Length; i++)
        {
            if (ValueOf(arguments[i]) is not { } value || DeclaredTypeOf(value) is not { } declared)
            {
                return null;
            }

            var type = parameters[i].ParameterType;
            if (type == typeof(string) && declared is VBStringType and not VBFixedStringType && IsStringItem(value))
            {
                if (Text(value) is not { } text)
                {
                    return null;
                }

                values.Add(new TextArgumentNode(text));
                continue;
            }

            var expected = type == typeof(long) ? IsWhole(declared) : type == typeof(double) && declared is VBDoubleType;
            if (!expected || IsVariant(value) || IsExtended(value) || Payload(value) is not { } payload)
            {
                return null;
            }

            values.Add(payload);
        }

        return values;
    }

    // an argument given as a value, by value or by reference, with no element list; and the only argument of a call, given so.
    private static BoundNode? ValueOf(BoundArgument argument)
        => argument is { Passing: ArgumentPassing.Value or ArgumentPassing.Reference, Value: { } value, Elements.IsDefaultOrEmpty: true } ? value : null;

    private static BoundNode? SoleArgumentOf(BoundCall call) => call.BoundArguments is [var sole] ? ValueOf(sole) : null;

    private VBType? DeclaredTypeOf(BoundNode value) => _evaluator.DeclaredTypeOf(_session, value.Expression, new RuntimeEvaluationContext(_procedure.Uri));

    private static bool IsWhole(VBType declared) => declared is VBByteType or VBIntegerType or VBLongType or VBLongLongType;

    // the library's CStr (IStdConversionModule.CStr), by the member the library declares it as.
    private static readonly string CStrFunction
        = StdLibSymbolReader.ExternalTargetOf(typeof(IStdConversionModule), typeof(IStdConversionModule).GetMethod(nameof(IStdConversionModule.CStr))!);

    // CStr of a whole number, outside an instruction that calls inside an expression: the number let-coerced to String (MS-VBAL §6.1.2.3.1.12), which is
    // its digits whatever the regional settings (WholeTextNode). None for any other call, and nothing is refused then.
    private TextNode? WholeTextOf(BoundCall call)
    {
        if (_nesting is not null || SoleArgumentOf(call) is not { } number
            || !StandardLibrary.IsLibraryMember(call.Procedure) || call.Procedure.GetProperty(SymbolProperties.ExternalTarget) != CStrFunction
            || IsVariant(number) || IsExtended(number) || DeclaredTypeOf(number) is not { } declared
            || !IsWhole(declared) || WholeOf(number) is not { } whole)
        {
            return null;
        }

        return whole is ConstantNode { Integral: var digits } ? new TextConstantNode(StringOperations.OfWhole(digits)) : new WholeTextNode(whole);
    }

    // the conversions to a numeric type (MS-VBAL §6.1.2.3.1), by name.
    private static readonly ImmutableHashSet<string> NumericConversions = ImmutableHashSet.Create(StringComparer.Ordinal,
        nameof(IStdConversionModule.CByte), nameof(IStdConversionModule.CInt), nameof(IStdConversionModule.CLng), nameof(IStdConversionModule.CLngLng),
        nameof(IStdConversionModule.CSng), nameof(IStdConversionModule.CDbl), nameof(IStdConversionModule.CCur));

    // a conversion to a numeric type of a whole number, or of a value of that type: the value let-coerced to it, as compiled code let-coerces a payload
    // (Converted), which declines a whole number out of the type's range. None for any other call, and nothing is refused then.
    private PayloadNode? ConversionOf(BoundCall call)
    {
        if (_nesting is not null || call.Procedure is not VBTypeMemberSymbol function || SoleArgumentOf(call) is not { } argument
            || !IsCompiledLibrary(function) || !NumericConversions.Contains(function.Name) || ScalarKinds.Of(ReturnedType(function)) is not { } to
            || IsVariant(argument) || IsExtended(argument) || DeclaredTypeOf(argument) is not { } declared
            || ScalarKinds.Of(declared) is not { } from
            || from != to && from is not (ScalarKind.Byte or ScalarKind.Integer or ScalarKind.Long or ScalarKind.LongLong)
            || Payload(argument) is not { } payload)
        {
            return null;
        }

        return Converted(payload, to);
    }
}
