using RDCore.Runtime.StdLib;
using RDCore.SDK.Runtime.Abstract.Execution;
using System.Reflection;
using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call of the typed form of a function of the standard library (StdLibIntrinsicAttribute): the form called with the payloads and
// texts of its arguments, as a method of the runtime.
internal sealed partial class ProcedureEmitter
{
    // the call of a form that returns a number: what it returns, held as a payload of its kind, a bool as a Boolean's -1 or 0.
    private Operand EmitIntrinsic(IntrinsicNode node, Label failure)
    {
        EmitIntrinsicCall(node.Method, node.Arguments, failure);
        var result = _il.DeclareLocal(ScalarKinds.IsIntegral(node.Kind) ? typeof(long) : typeof(double));
        if (node.Kind is ScalarKind.Boolean)
        {
            // true (1) is -1.
            _il.Emit(OpCodes.Neg);
        }

        if (ScalarKinds.IsIntegral(node.Kind))
        {
            _il.Emit(OpCodes.Conv_I8);
        }

        _il.Emit(OpCodes.Stloc, result);
        return new Operand(result, 0, 0);
    }

    // the call of a form that returns a String: its text, in the local text.
    private void EmitIntrinsicText(IntrinsicTextNode node, LocalBuilder text, Label failure)
    {
        EmitIntrinsicCall(node.Method, node.Arguments, failure);
        _il.Emit(OpCodes.Stloc, text);
    }

    // the characters of an expression of Strings, in a local of their own (a ReadOnlySpan<char>): those of the part of a text Mid$, Left$ or Right$ takes
    // read where the text holds them, with no String made (StdStrings.MiddleSpanOf, LeftSpanOf, RightSpanOf), and those of the text any other expression is.
    // A Start below 1 or a negative Length goes to failure, as the form that makes the String declines it.
    private LocalBuilder EmitCharactersInto(TextNode node, Label failure)
    {
        var characters = _il.DeclareLocal(typeof(ReadOnlySpan<char>));
        if (node is IntrinsicTextNode typed && SpanForms.TryGetValue(typed.Method, out var spanForm))
        {
            EmitIntrinsicCall(spanForm, typed.Arguments, failure);
        }
        else
        {
            _il.Emit(OpCodes.Ldloc, EmitTextInto(node, failure));
            _il.Emit(OpCodes.Call, AsSpan);
        }

        _il.Emit(OpCodes.Stloc, characters);
        return characters;
    }

    private static readonly MethodInfo AsSpan = typeof(MemoryExtensions).GetMethod(nameof(MemoryExtensions.AsSpan), [typeof(string)])!;

    // the forms that read the characters of a part of a text where the text holds it, by the form that makes the part a String, of the same parameters.
    private static readonly Dictionary<MethodInfo, MethodInfo> SpanForms = new[]
        {
            (Text: nameof(StdStrings.MiddleOf), Span: nameof(StdStrings.MiddleSpanOf)),
            (Text: nameof(StdStrings.LeftOf), Span: nameof(StdStrings.LeftSpanOf)),
            (Text: nameof(StdStrings.RightOf), Span: nameof(StdStrings.RightSpanOf)),
        }
        .SelectMany(names => typeof(StdStrings).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => method.Name == names.Text),
            (names, form) => (Form: form, Span: SpanFormOf(form, names.Span)))
        .ToDictionary(pair => pair.Form, pair => pair.Span);

    // the form named span of the parameters of a form that makes a String, its out String a ReadOnlySpan<char>.
    private static MethodInfo SpanFormOf(MethodInfo form, string span)
        => typeof(StdStrings).GetMethod(span,
                [.. form.GetParameters()[..^1].Select(parameter => parameter.ParameterType), typeof(ReadOnlySpan<char>).MakeByRefType()])
            ?? throw new InvalidOperationException($"{form.Name} has no {span} of its parameters");

    // the arguments evaluated into locals of the parameters' types (a whole number is held as a long, which an argument that widens narrows from with no
    // loss), and the activation's session for a form that reads it; then the call, which leaves what the form returns on the stack. A form that returns a
    // bool and its result in an out parameter declines with false, which goes to failure: the interpreter makes the call.
    private void EmitIntrinsicCall(MethodInfo method, IReadOnlyList<PayloadNode> given, Label failure)
    {
        var parameters = method.GetParameters();
        var arguments = new List<LocalBuilder?>(parameters.Length);
        var next = 0;
        foreach (var parameter in parameters.Where(parameter => !parameter.IsOut))
        {
            var type = parameter.ParameterType;
            if (type == typeof(IRuntimeSession))
            {
                arguments.Add(null);
                continue;
            }

            var payload = given[next++];
            if (payload is TextArgumentNode { Value: var text })
            {
                arguments.Add(type == typeof(ReadOnlySpan<char>) ? EmitCharactersInto(text, failure) : EmitTextInto(text, failure));
                continue;
            }

            var operand = EmitPayload(payload, failure);
            var argument = _il.DeclareLocal(type);
            Load(operand, payload.Kind);
            if (type == typeof(double) && ScalarKinds.IsIntegral(payload.Kind))
            {
                _il.Emit(OpCodes.Conv_R8);
            }
            else if (type == typeof(int))
            {
                _il.Emit(OpCodes.Conv_I4);
            }
            else if (type == typeof(short))
            {
                _il.Emit(OpCodes.Conv_I2);
            }

            _il.Emit(OpCodes.Stloc, argument);
            arguments.Add(argument);
        }

        foreach (var argument in arguments)
        {
            if (argument is null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Call, Activation.GetProperty(nameof(CompiledActivation.Session))!.GetMethod!);
                continue;
            }

            _il.Emit(OpCodes.Ldloc, argument);
        }

        var declines = parameters is [.., { IsOut: true }];
        var returned = declines ? _il.DeclareLocal(parameters[^1].ParameterType.GetElementType()!) : null;
        if (returned is not null)
        {
            _il.Emit(OpCodes.Ldloca, returned);
        }

        _il.Emit(OpCodes.Call, method);
        if (returned is not null)
        {
            _il.Emit(OpCodes.Brfalse, Fail(failure));
            _il.Emit(OpCodes.Ldloc, returned);
        }
    }
}
