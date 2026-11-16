using System.Reflection;
using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call of the typed form of a function of the standard library (StdLibIntrinsicAttribute): the form called with the payloads and
// texts of its arguments, as a method of the runtime.
internal sealed partial class ProcedureEmitter
{
    // the call of a form that returns a number: what it returns, held as a payload of its kind.
    private Operand EmitIntrinsic(IntrinsicNode node, Label failure)
    {
        EmitIntrinsicCall(node.Method, node.Arguments, failure);
        var result = _il.DeclareLocal(ScalarKinds.IsIntegral(node.Kind) ? typeof(long) : typeof(double));
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

    // the arguments evaluated into locals of the parameters' types (a whole number is held as a long, which an argument that widens narrows from with no
    // loss); then the call, which leaves what the form returns on the stack. A form that returns a bool and its result in an out parameter declines with
    // false, which goes to failure: the interpreter makes the call.
    private void EmitIntrinsicCall(MethodInfo method, IReadOnlyList<PayloadNode> given, Label failure)
    {
        var parameters = method.GetParameters();
        var arguments = new List<LocalBuilder>(given.Count);
        for (var i = 0; i < given.Count; i++)
        {
            if (given[i] is TextArgumentNode { Value: var text })
            {
                arguments.Add(EmitTextInto(text, failure));
                continue;
            }

            var payload = given[i];
            var operand = EmitPayload(payload, failure);
            var argument = _il.DeclareLocal(parameters[i].ParameterType);
            Load(operand, payload.Kind);
            var type = parameters[i].ParameterType;
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
            _il.Emit(OpCodes.Ldloc, argument);
        }

        var declines = parameters is [.., { IsOut: true }];
        var result = declines ? _il.DeclareLocal(parameters[^1].ParameterType.GetElementType()!) : null;
        if (result is not null)
        {
            _il.Emit(OpCodes.Ldloca, result);
        }

        _il.Emit(OpCodes.Call, method);
        if (result is not null)
        {
            _il.Emit(OpCodes.Brfalse, Fail(failure));
            _il.Emit(OpCodes.Ldloc, result);
        }
    }
}
