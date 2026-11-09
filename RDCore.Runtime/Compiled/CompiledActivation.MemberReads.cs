using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a public variable of an object it reads, and of Err.Number.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Reads <c>Err.Number</c> (MS-VBAL §6.1.3.2.2.5): the number of the session's error, as the error object reads it. Not under a debugger that observes
    /// member accesses, which the interpreter reports it to.
    /// </summary>
    /// <param name="number">The number.</param>
    public bool TryReadErrNumber(out long number)
    {
        number = _session.Errors.Number;
        return _executor.Observation is null;
    }

    /// <summary>
    /// Reads the public variable numbered <paramref name="number"/> of the body (MS-VBAL §5.6.12), of the object the variable whose cell is
    /// <paramref name="cell"/> refers to: the variable the object's class has for it is found as the interpreter finds it (<c>EvaluateBoundMember</c>), and
    /// read in its cell. Not when the variable holds <c>Nothing</c> (error 91, which the interpreter raises), refers to an object the session has no
    /// instance of, or whose class has a procedure for it, or a variable of another kind: the interpreter reads it.
    /// </summary>
    /// <param name="number">The number of the variable among the body's.</param>
    /// <param name="cell">The cell of the object variable the variable is read of.</param>
    /// <param name="integral">What it holds, of an integral kind.</param>
    /// <param name="floating">What it holds, of a floating-point kind.</param>
    public bool TryReadMemberField(int number, object? cell, out long integral, out double floating)
        => TryReadFieldOf(number,
            _executor.Observation is null && ObjectOperations.TryRead(cell, out var id) && !id.Equals(default(VBRuntimeObjectId))
                && _session.Symbols.TryGetInstance(id, out var instance) ? instance : null,
            out integral, out floating);

    /// <summary>
    /// Reads the public variable numbered <paramref name="number"/> of the body, of the object of the <c>With</c> block opened at
    /// <paramref name="opener"/> (MS-VBAL §5.4.2.21), as <see cref="TryReadMemberField"/> reads one of the object a variable refers to. The object is the
    /// one the interpreter holds for the block, where it holds it: a block that holds none, or <c>Nothing</c>, is the interpreter's, which raises error 91.
    /// </summary>
    /// <param name="number">The number of the variable among the body's.</param>
    /// <param name="opener">The offset of the <c>With</c> statement of the block.</param>
    /// <param name="integral">What it holds, of an integral kind.</param>
    /// <param name="floating">What it holds, of a floating-point kind.</param>
    public bool TryReadMemberFieldOfWith(int number, int opener, out long integral, out double floating)
        => TryReadFieldOf(number,
            _executor.Observation is null && Frame.TryGetBlockState(opener, out var held)
                && (held is VBVariantValue { TypedValue: var inner } ? inner : held) is VBObjectValue target && !target.IsNothing()
                && _session.Symbols.TryGetInstance(target.Value, out var instance) ? instance : null,
            out integral, out floating);

    private bool TryReadFieldOf(int number, IObjectInstance? instance, out long integral, out double floating)
    {
        (integral, floating) = (0, 0);
        var read = _body.MemberFields[number];
        if (instance is null || _executor.Expressions.MemberOf(read.Site, instance.ClassModule) is not VBModuleFieldVariableMemberSymbol field
            || !instance.TryResolve(field, out var held) || held is not ScalarCell cell || ScalarKinds.Of(cell.Type) != read.Kind
            || !cell.TryRead(out var payload))
        {
            return false;
        }

        (integral, floating) = (payload.Integral, payload.Floating);
        return true;
    }
}
