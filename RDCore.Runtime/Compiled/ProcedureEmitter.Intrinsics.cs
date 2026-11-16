using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a call of the typed form of a function of the standard library (StdLibIntrinsicAttribute): the form called with the payloads and
// texts of its arguments, as a method of the runtime.
internal sealed partial class ProcedureEmitter
{
    // the arguments evaluated into locals of the parameters' types (a whole number is held as a long, which an argument that widens narrows from with no
    // loss); then the call. A form that returns a bool and its result in an
    // out parameter declines with false, which goes to failure: the interpreter makes the call. What it returns is held as a payload of its kind.
    private Operand EmitIntrinsic(IntrinsicNode node, Label failure)
    {
        var parameters = node.Method.GetParameters();
        var arguments = new List<LocalBuilder>(node.Arguments.Count);
        for (var i = 0; i < node.Arguments.Count; i++)
        {
            if (node.Arguments[i] is TextArgumentNode { Value: var text })
            {
                arguments.Add(EmitTextInto(text, failure));
                continue;
            }

            var payload = node.Arguments[i];
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
        var given = declines ? _il.DeclareLocal(parameters[^1].ParameterType.GetElementType()!) : null;
        if (given is not null)
        {
            _il.Emit(OpCodes.Ldloca, given);
        }

        _il.Emit(OpCodes.Call, node.Method);
        if (given is not null)
        {
            _il.Emit(OpCodes.Brfalse, failure);
            _il.Emit(OpCodes.Ldloc, given);
        }

        var result = _il.DeclareLocal(ScalarKinds.IsIntegral(node.Kind) ? typeof(long) : typeof(double));
        if (ScalarKinds.IsIntegral(node.Kind))
        {
            _il.Emit(OpCodes.Conv_I8);
        }

        _il.Emit(OpCodes.Stloc, result);
        return new Operand(result, 0, 0);
    }
}
