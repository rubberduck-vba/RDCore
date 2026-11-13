using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a public variable of an object it reads, and of Err.Number, Err.Description and Err.Source.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// Reads <c>Err.Number</c> (MS-VBAL §6.1.3.2.2.5): the number of the session's error, as the error object reads it. Not under a debugger that observes
    /// member accesses: the interpreter reads it then, and reports the access.
    /// </summary>
    /// <param name="number">The number.</param>
    public bool TryReadErrNumber(out long number)
    {
        number = _session.Errors.Number;
        return _executor.Observation is null;
    }

    /// <summary>
    /// Reads <c>Err.Description</c> (MS-VBAL §6.1.3.2.2.1), or <c>Err.Source</c> (MS-VBAL §6.1.3.2.2.6): the text of the session's error, as the error object
    /// reads it. Not under a debugger that observes member accesses: the interpreter reads it then, and reports the access.
    /// </summary>
    /// <param name="source">Whether <c>Err.Source</c> is read; otherwise, <c>Err.Description</c>.</param>
    /// <param name="text">The text.</param>
    public bool TryReadErrText(bool source, out string text)
    {
        text = source ? _session.Errors.Source : _session.Errors.Description;
        return _executor.Observation is null;
    }

    /// <summary>
    /// Reads the public variable numbered <paramref name="number"/> of the body (MS-VBAL §5.6.12), of the object the variable whose cell is
    /// <paramref name="cell"/> refers to: the variable the object's class has for it is found as the interpreter finds it (<c>EvaluateBoundMember</c>), and
    /// read in its cell. Not under a debugger that observes member accesses, not when the variable holds <c>Nothing</c> (error 91, which the interpreter
    /// raises) or an object the session has no instance of, and not when the object's class has a procedure for the member, or a variable of a kind other
    /// than the one compiled code reads: the interpreter reads it then.
    /// </summary>
    /// <param name="number">The number of the variable among the body's.</param>
    /// <param name="cell">The cell of the object variable the variable is read of.</param>
    /// <param name="integral">What it holds, of an integral kind.</param>
    /// <param name="floating">What it holds, of a floating-point kind.</param>
    public bool TryReadMemberField(int number, object? cell, out long integral, out double floating)
        => TryReadFieldOf(number, InstanceIn(cell), out integral, out floating);

    /// <summary>
    /// Reads the <c>String</c> public variable numbered <paramref name="number"/> of the body, of variable length, of the object the variable whose cell is
    /// <paramref name="cell"/> refers to, as <see cref="TryReadMemberField"/> reads a scalar one: the text its cell holds. Not when the cell holds no text
    /// compiled code reads (<see cref="StringOperations.TryRead"/>): the interpreter reads it then.
    /// </summary>
    /// <param name="number">The number of the variable among the body's.</param>
    /// <param name="cell">The cell of the object variable the variable is read of.</param>
    /// <param name="text">The text; <see langword="null"/> for <c>vbNullString</c>.</param>
    public bool TryReadMemberText(int number, object? cell, out string? text) => TryReadTextOf(number, InstanceIn(cell), out text);

    // the object the object variable whose cell is cell refers to, when compiled code reads a variable of it: none under a debugger that observes member
    // accesses, nor of Nothing, nor of an object the session has no instance of.
    private IObjectInstance? InstanceIn(object? cell)
        => _executor.Observation is null && ObjectOperations.TryRead(cell, out var id) && !id.Equals(default(VBRuntimeObjectId))
            && _session.Symbols.TryGetInstance(id, out var instance) ? instance : null;

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
        => TryReadFieldOf(number, InstanceOfBlock(opener), out integral, out floating);

    /// <summary>
    /// Reads the <c>String</c> public variable numbered <paramref name="number"/> of the body, of the object of the <c>With</c> block opened at
    /// <paramref name="opener"/>, as <see cref="TryReadMemberText"/> reads one of the object a variable refers to.
    /// </summary>
    /// <param name="number">The number of the variable among the body's.</param>
    /// <param name="opener">The offset of the <c>With</c> statement of the block.</param>
    /// <param name="text">The text; <see langword="null"/> for <c>vbNullString</c>.</param>
    public bool TryReadMemberTextOfWith(int number, int opener, out string? text) => TryReadTextOf(number, InstanceOfBlock(opener), out text);

    // the object the interpreter holds for the With block opened at opener, where it holds it: none for a block that holds none, or Nothing.
    private IObjectInstance? InstanceOfBlock(int opener)
        => _executor.Observation is null && Frame.TryGetBlockState(opener, out var held)
            && (held is VBVariantValue { TypedValue: var inner } ? inner : held) is VBObjectValue target && !target.IsNothing()
            && _session.Symbols.TryGetInstance(target.Value, out var instance) ? instance : null;

    // the text of a String public variable of variable length of instance, in its cell.
    private bool TryReadTextOf(int number, IObjectInstance? instance, out string? text)
    {
        text = null;
        var read = _body.MemberFields[number];
        return instance is not null && _executor.Expressions.MemberOf(read.Site, instance.ClassModule) is VBModuleFieldVariableMemberSymbol { ResolvedType: VBStringType and not VBFixedStringType } field
            && instance.TryResolve(field, out var held) && StringOperations.TryRead(held, out text);
    }

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

    /// <summary>
    /// Writes the public variable numbered <paramref name="number"/> of the body (MS-VBAL §5.4.3.8), of the object the variable whose cell is
    /// <paramref name="cell"/> refers to, as the interpreter assigns it (<c>LetAssignmentEvaluator.AssignObjectMember</c>): the variable the object's class
    /// has for it is found as <see cref="TryReadMemberField"/> finds it, and given the value, already let-coerced to its kind, in its cell. Not when
    /// <see cref="TryReadMemberField"/> would not read it: the interpreter assigns it then, and makes the object of a variable declared <c>As New</c> that
    /// holds none, raises error 91 for <c>Nothing</c>, or calls the <c>Property Let</c> a class that implements the declared one has for it.
    /// </summary>
    /// <param name="number">The number of the variable among the body's.</param>
    /// <param name="cell">The cell of the object variable the variable is written of.</param>
    /// <param name="integral">The value, of an integral kind.</param>
    /// <param name="floating">The value, of a floating-point kind.</param>
    public bool TryLetMemberField(int number, object? cell, long integral, double floating) => TryLetFieldOf(number, InstanceIn(cell), integral, floating);

    /// <summary>
    /// Writes the public variable numbered <paramref name="number"/> of the body, of the object of the <c>With</c> block opened at
    /// <paramref name="opener"/> (MS-VBAL §5.4.2.21), as <see cref="TryLetMemberField"/> writes one of the object a variable refers to.
    /// </summary>
    /// <param name="number">The number of the variable among the body's.</param>
    /// <param name="opener">The offset of the <c>With</c> statement of the block.</param>
    /// <param name="integral">The value, of an integral kind.</param>
    /// <param name="floating">The value, of a floating-point kind.</param>
    public bool TryLetMemberFieldOfWith(int number, int opener, long integral, double floating)
        => TryLetFieldOf(number, InstanceOfBlock(opener), integral, floating);

    // the value written in the cell of a scalar public variable of instance, of the kind it was let-coerced to; nothing written when the cell is of another kind.
    private bool TryLetFieldOf(int number, IObjectInstance? instance, long integral, double floating)
    {
        var let = _body.MemberFields[number];
        if (FailsAccessAtRandom() || instance is null
            || _executor.Expressions.MemberOf(let.Site, instance.ClassModule) is not VBModuleFieldVariableMemberSymbol field
            || !instance.TryResolve(field, out var held) || held is not ScalarCell cell || ScalarKinds.Of(cell.Type) != let.Kind)
        {
            return false;
        }

        cell.Write(new ScalarPayload(cell.Type, integral, floating));
        return true;
    }
}
