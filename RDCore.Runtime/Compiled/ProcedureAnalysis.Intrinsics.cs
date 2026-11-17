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
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Immutable;
using System.Reflection;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call of a function of the standard library whose arguments it knows the types of: the typed form of the function
// (StdLibIntrinsicAttribute), called with no Variant and no frame; and a conversion of a whole number, which is the number let-coerced to the conversion's
// type (MS-VBAL §6.1.2.3.1), as compiled code let-coerces it.
internal sealed partial class ProcedureAnalysis
{
    // a typed form, and for each of its parameters whether the function let-coerces an argument to it: one it declares of a type of its own, or a Variant
    // the attribute says it let-coerces (StdLibIntrinsicAttribute.LetCoerces); any other Variant keeps the argument's type.
    private readonly record struct IntrinsicForm(MethodInfo Method, ImmutableArray<bool> Coerced);

    // the typed forms of the library's functions, by the function each is of: the methods of the implementations of the library's modules that carry the
    // attribute, once for each function it names.
    private static readonly ImmutableDictionary<string, ImmutableArray<IntrinsicForm>> Intrinsics = typeof(StdStrings).Assembly.GetTypes()
        .Where(type => type.Namespace == typeof(StdStrings).Namespace)
        .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        .SelectMany(method => method.GetCustomAttributes<StdLibIntrinsicAttribute>(), (method, intrinsic) => (Method: method, Intrinsic: intrinsic))
        .Select(marked => (marked.Method, marked.Intrinsic, Declaration: marked.Intrinsic.Module.GetMethod(marked.Intrinsic.Member)!))
        .GroupBy(
            marked => StdLibSymbolReader.ExternalTargetOf(marked.Intrinsic.Module, marked.Declaration),
            marked => new IntrinsicForm(marked.Method,
                [.. marked.Declaration.GetParameters().Select(parameter => marked.Intrinsic.LetCoerces || parameter.ParameterType != typeof(VBVariantValue))]))
        .ToImmutableDictionary(group => group.Key, group => group.ToImmutableArray(), StringComparer.Ordinal);

    // a call of a function of the library that has a typed form of the types of its arguments that returns a number: the call of that form. None when the
    // function has none of those types; nothing is refused then, and the call is made as any other.
    private IntrinsicNode? IntrinsicOf(BoundNode node)
        => FormOf(node, method => ResultKindOf(method) is not null) is (var method, var values)
            ? new IntrinsicNode(method, values, ResultKindOf(method)!.Value)
            : null;

    // the same, of a typed form that returns a String: its text.
    private IntrinsicTextNode? IntrinsicTextOf(BoundNode node)
        => FormOf(node, method => ResultTypeOf(method) == typeof(string)) is (var method, var values) ? new IntrinsicTextNode(method, values) : null;

    // the typed form of a call of a function of the library whose result type the returns predicate accepts, and the arguments given to it, outside an
    // instruction that calls inside an expression.
    private (MethodInfo Method, List<PayloadNode> Arguments)? FormOf(BoundNode node, Func<MethodInfo, bool> returns)
    {
        // Abs, Int or Fix of an extended intermediate is itself one, computed on its 80 bits (Extended): never a typed form, which is given the argument
        // stored.
        if (_nesting is not null || IsExtended(node) || node is not BoundCall { Procedure: var function, BoundArguments: { } arguments }
            || !StandardLibrary.IsLibraryMember(function) || function.GetProperty(SymbolProperties.ExternalTarget) is not { } target
            || !Intrinsics.TryGetValue(target, out var methods))
        {
            return null;
        }

        foreach (var form in methods)
        {
            if (returns(form.Method) && IntrinsicArgumentsOf(form, arguments) is { } values)
            {
                return (form.Method, values);
            }
        }

        return null;
    }

    // what a typed form returns, whether it is returned or given back in an out parameter: its type, and the return value or the parameter it is; and that of
    // a payload's kind.
    private static (Type? Type, ParameterInfo Result) ResultOf(MethodInfo method)
        => method.ReturnType == typeof(bool) && method.GetParameters() is [.., { IsOut: true } result]
            ? (result.ParameterType.GetElementType(), result)
            : (method.ReturnType, method.ReturnParameter);

    private static Type? ResultTypeOf(MethodInfo method) => ResultOf(method).Type;

    private static ScalarKind? ResultKindOf(MethodInfo method)
    {
        var (type, result) = ResultOf(method);
        return KindOf(type, result);
    }

    // the payload kind a type of a typed form's parameter or result is: a short is an Integer, an int a Long, a long a LongLong, a double a Double (a Date where
    // the parameter or the return value is marked StdLibDateAttribute), a bool a Boolean.
    private static ScalarKind? KindOf(Type? type, ParameterInfo parameter)
        => type == typeof(short) ? ScalarKind.Integer
            : type == typeof(int) ? ScalarKind.Long
            : type == typeof(long) ? ScalarKind.LongLong
            : type == typeof(double) ? parameter.IsDefined(typeof(StdLibDateAttribute)) ? ScalarKind.Date : ScalarKind.Double
            : type == typeof(bool) ? ScalarKind.Boolean
            : null;

    // whether an argument of kind given is let-coerced to a parameter of kind taken with no loss: a whole number to a wider one, or to a Double.
    private static bool Widens(ScalarKind given, ScalarKind taken) => taken switch
    {
        ScalarKind.Double => given is ScalarKind.Byte or ScalarKind.Integer or ScalarKind.Long or ScalarKind.LongLong,
        ScalarKind.LongLong => given is ScalarKind.Byte or ScalarKind.Integer or ScalarKind.Long,
        ScalarKind.Long => given is ScalarKind.Byte or ScalarKind.Integer,
        ScalarKind.Integer => given is ScalarKind.Byte,
        _ => false,
    };

    // the arguments of a call given to a typed form, when each is of the type of its parameter: a String's text for a string, a payload of the parameter's
    // kind for a number. A parameter the function declares of a type of its own also takes a number that widens to it, as the call would let-coerce it; one
    // it declares a Variant takes only its own kind, its type being what the function returns. Each argument is given, by value: the form has no optional
    // parameter, and a function with one has a form for each number of arguments it takes, except a Compare the call leaves out
    // (StdLibOptionCompareAttribute), which is given the calling module's Option Compare. The session, which the activation gives a form that reads it, is not
    // one of the call's arguments.
    private List<PayloadNode>? IntrinsicArgumentsOf(IntrinsicForm form, ImmutableArray<BoundArgument> arguments)
    {
        // arguments left out at the end are dropped, as a call of the library's binding drops them (LibraryArgumentsOf): Round(D) is the form of one. A
        // comparison left out that is the calling module's Option Compare goes to a form that takes a Compare (StrComp) as that module's (below); a form
        // that takes none compares Strings as Option Compare Binary does, so it takes the call only in a module that compares that way (InStr).
        var parameters = form.Method.GetParameters().Where(parameter => !parameter.IsOut && parameter.ParameterType != typeof(IRuntimeSession)).ToArray();
        var count = arguments.Length;
        while (count > 0 && arguments[count - 1].Passing is ArgumentPassing.Omitted)
        {
            var takesCompare = count - 1 < parameters.Length && parameters[count - 1].GetCustomAttribute<StdLibOptionCompareAttribute>() is not null;
            if (!takesCompare && arguments[count - 1].Parameter.TryGetProperty(SymbolProperties.DefaultsToOptionCompare, out var callers) && callers
                && CallersCompareMethod() is not VBCompareMethod.VBBinaryCompare)
            {
                return null;
            }

            count--;
        }

        if (count > parameters.Length || form.Coerced.Length < parameters.Length)
        {
            return null;
        }

        var values = new List<PayloadNode>(parameters.Length);
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i >= count)
            {
                if (parameters[i].GetCustomAttribute<StdLibOptionCompareAttribute>() is null || CallersCompareMethod() is not { } compare)
                {
                    return null;
                }

                values.Add(new ConstantNode(ScalarKind.Long, (long)compare, 0));
                continue;
            }

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

            // an extended intermediate is given as the call holds it: stored as the expression's type from its 80 bits, which raises what it raises there
            // (StoreNode), then given as any value of that type.
            if (KindOf(type, parameters[i]) is not { } taken || ScalarKinds.Of(declared) is not { } given
                || given != taken && !(form.Coerced[i] && Widens(given, taken)) || IsVariant(value) || (IsExtended(value) ? PayloadAs(value, given) : Payload(value)) is not { } payload)
            {
                return null;
            }

            values.Add(payload);
        }

        return values;
    }

    // MS-VBAL §6.1.2.11: the Compare a comparing function's call leaves out, which "uses the <option-compare-directive> of the calling module": the module
    // declaring the procedure, as the interpreter reads it from the procedure's frame (RuntimeExpressionEvaluator's omitted argument). Option Compare
    // Database compares as the environment says, which the interpreter reads when the call is made.
    private VBCompareMethod? CallersCompareMethod()
    {
        var directives = _session.Symbols.TryGetModule(SymbolUri.ModuleNameOf(_procedure.ParentUri), out var module) ? module.Directives : ModuleDirectives.None;
        return directives.Compare switch
        {
            OptionCompare.Binary => VBCompareMethod.VBBinaryCompare,
            OptionCompare.Text => VBCompareMethod.VBTextCompare,
            _ => null,
        };
    }

    // an argument given as a value, by value or by reference, with no element list; and the only argument of a call, given so.
    private static BoundNode? ValueOf(BoundArgument argument)
        => argument is { Passing: ArgumentPassing.Value or ArgumentPassing.Reference, Value: { } value, Elements.IsDefaultOrEmpty: true } ? value : null;

    private static BoundNode? SoleArgumentOf(BoundCall call) => call.BoundArguments is [var sole] ? ValueOf(sole) : null;

    private VBType? DeclaredTypeOf(BoundNode value) => _evaluator.DeclaredTypeOf(_session, value.Expression, new RuntimeEvaluationContext(_procedure.Uri));

    private static bool IsWhole(VBType declared) => declared is VBByteType or VBIntegerType or VBLongType or VBLongLongType;

    private static bool IsWholeKind(ScalarKind kind) => kind is ScalarKind.Byte or ScalarKind.Integer or ScalarKind.Long or ScalarKind.LongLong;

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

    // a conversion to a numeric type of a whole number, of a Single or a Double to an integral type, or of a value of that type: the value let-coerced to
    // it, as compiled code let-coerces a payload (Converted): a whole number out of the type's range declines, and a Single or a Double is rounded half to
    // even, one out of the range being an overflow (MS-VBAL §5.5.1.2.1). None for any other call, and nothing is refused then.
    private PayloadNode? ConversionOf(BoundCall call)
    {
        if (_nesting is not null || call.Procedure is not VBTypeMemberSymbol function || SoleArgumentOf(call) is not { } argument
            || !IsCompiledLibrary(function) || !NumericConversions.Contains(function.Name) || ScalarKinds.Of(ReturnedType(function)) is not { } to
            || IsVariant(argument) || IsExtended(argument) || DeclaredTypeOf(argument) is not { } declared
            || ScalarKinds.Of(declared) is not { } from
            || from != to && !IsWholeKind(from) && !(from is ScalarKind.Single or ScalarKind.Double && IsWholeKind(to))
            || Payload(argument) is not { } payload)
        {
            return null;
        }

        return Converted(payload, to);
    }
}
