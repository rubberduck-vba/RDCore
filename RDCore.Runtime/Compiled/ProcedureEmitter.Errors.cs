using System.Reflection.Emit;

namespace RDCore.Runtime.Compiled;

// what compiled code does of an error an operation it evaluates finds: at an instruction the interpreter is given alone when it fails, the error is raised
// where compiled code found it (CompiledActivation.RaiseArithmetic), and the interpreter does not evaluate the instruction again to find it. An error the
// activation's On Error Resume Next is given where compiled code is goes on at the next instruction (CompiledActivation.TryResumeNext).
internal sealed partial class ProcedureEmitter
{
    // whether the step being emitted raises the error an operation of it finds (RaisesErrorsIn).
    private bool _raisesErrors;

    // a step whose instruction the interpreter gives a state to as it evaluates it, when it fails (a For loop its end and step, ProcedureExecutor.Unevaluated;
    // a Select its selector), is evaluated by the interpreter on an error: the state is what a Resume Next goes on with. Any other step's error is raised
    // where compiled code found it.
    private static bool RaisesErrorsIn(CompiledStep step)
        => step is not (ForOpenStep or ForNextStep or ForEachOpenStep or ForEachNextStep or VariantForNextStep or SelectStep or TextSelectStep or CaseStep);

    // the Let being emitted, of a Single or a Double variable: what IEEE 754 arithmetic made of the operation its value is, when that raised an error, is
    // assigned to the variable before the error is raised, as the interpreter assigns it (StatementRuntimeSemanticsProvider.AssignFloatingPointValue).
    private Action<Operand>? _assignsOnError;

    // the value of a Let, evaluated: assign is how the Let assigns it, which a Single or Double value of an operation that raised an error is given first.
    private Operand EmitAssigned(LetStep let, Action<Operand> assign)
    {
        var failure = FailureOf(let.Value, let.Offset);
        _assignsOnError = let.Value is StoreNode { Kind: ScalarKind.Single or ScalarKind.Double } ? assign : null;
        try
        {
            return EmitPayload(let.Value, failure);
        }
        finally
        {
            _assignsOnError = null;
        }
    }

    // what the Let being emitted assigns on an error, for the operation emitted now, which is its value: an operation inside it is not.
    private Action<Operand>? TakeAssignsOnError()
    {
        var assign = _assignsOnError;
        _assignsOnError = null;
        return assign;
    }

    // the site the error of a conversion is raised at: none, for a Single or a Double value the Let being emitted does not assign on an error (a value inside
    // its value, or the value of an element or of a field), which the interpreter evaluates and assigns what IEEE 754 arithmetic made of it.
    private static int RaisedAt(StoreNode store, Action<Operand>? assign)
        => assign is null && store.Kind is ScalarKind.Single or ScalarKind.Double ? -1 : store.Site;

    // the status of an operation, on the stack, which gave result: Ok goes on. Where failure is the interpreter's run of the instruction alone (RecoverAt),
    // an error is given to it as the error of a call is, after assign is given result; anywhere else, and of any other status, the failure is the
    // interpreter's as it always is. A site below 0 raises nothing: the interpreter finds the error.
    private void EmitArithmeticStatus(int site, Label failure, LocalBuilder? result = null, Action<Operand>? assign = null)
    {
        if (site < 0 || !_raisesErrors || !_recoveries.ContainsValue(failure))
        {
            _il.Emit(OpCodes.Brtrue, Fail(failure));
            return;
        }

        var status = _il.DeclareLocal(typeof(int));
        var succeeded = _il.DefineLabel();
        _il.Emit(OpCodes.Stloc, status);
        _il.Emit(OpCodes.Ldloc, status);
        _il.Emit(OpCodes.Brfalse, succeeded);
        if (assign is not null && result is not null)
        {
            // a status below 0 declines, and is not an error: the interpreter evaluates the instruction, and assigns what it does.
            var declined = _il.DefineLabel();
            _il.Emit(OpCodes.Ldloc, status);
            _il.Emit(OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Ble, declined);
            assign(new Operand(result, 0, 0));
            _il.MarkLabel(declined);
        }

        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldloc, status);
        _il.Emit(OpCodes.Ldc_I4, site);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.RaiseArithmetic))!);

        // an error the activation's On Error Resume Next is given where compiled code is goes on at the next instruction; any other, the interpreter's.
        var offset = _recoveries.First(recovery => recovery.Value == failure).Key;
        _il.Emit(OpCodes.Ldarg_0);
        _il.Emit(OpCodes.Ldc_I4, offset);
        _il.Emit(OpCodes.Call, Activation.GetMethod(nameof(CompiledActivation.TryResumeNext))!);
        _il.Emit(OpCodes.Brtrue, _instructions[offset + 1]);
        _il.Emit(OpCodes.Br, Fail(failure));
        _il.MarkLabel(succeeded);
    }
}
