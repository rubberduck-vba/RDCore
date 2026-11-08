using RDCore.Runtime.Execution;
using RDCore.Runtime.Execution.Memory;

namespace RDCore.Runtime.Compiled;

// where compiled code finds the cell of a field it reaches, and how long it keeps it: the scope rule. What a cell found is - which cells compiled code takes
// for a field of a kind - is CompiledActivation.FieldKinds.cs's.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// The cell of the field numbered <paramref name="number"/>: the one the interpreter resolves the variable to, which compiled
    /// code reads and writes it in: the object's, for a variable of <c>Me</c>'s class or a <c>Static</c> variable of a member of it, and the session's, for
    /// a variable of a standard module or a <c>Static</c> variable of a procedure of one.
    /// </summary>
    /// <param name="number">The number the body knows the field by.</param>
    /// <returns><see langword="null"/> when it is held in no cell compiled code takes for its kind: the activation is then the interpreter's.</returns>
    public object? FieldCellOf(int number)
    {
        var field = _body.Fields[number];
        return field.Scope is FieldScope.Object ? ObjectFieldCellOf(number, field) : SessionFieldCellOf(number, field);
    }

    /// <summary>
    /// The cell of a field (<see cref="FieldCellOf"/>), found again after the interpreter ran an instruction or the program waited.
    /// </summary>
    /// <param name="number">The number the body knows the field by.</param>
    /// <returns><see langword="null"/> when the frame cannot be read, or the field is held in no cell compiled code takes for its kind.</returns>
    public object? ReloadFieldCell(int number) => FailsAccessAtRandom() ? null : FieldCellOf(number);

    // Me is the object the activation was entered on (EnterMemberDirectly), or the one its frame is a call on, which the interpreter resolves a field on.
    private object? ObjectFieldCellOf(int number, CompiledField field)
    {
        var instance = _instance ?? (Frame.Target is { } target && _session.Symbols.TryGetInstance(target, out var on) ? on : null);
        if (instance is null)
        {
            return null;
        }

        // the cell the body found last of the field of the same object, which keeps it for as long as it lives (CompiledBody.ObjectFields).
        var known = _body.ObjectFields;
        if (known is null || !known.Object.Equals(instance.ObjectId))
        {
            known = new FieldCells(instance.ObjectId, new object?[_body.Fields.Count]);
            _body.ObjectFields = known;
        }

        if (known.Cells[number] is { } cached)
        {
            return cached;
        }

        var found = instance.TryResolve(field.Symbol, out var held) ? CellOfKind(field, held) : null;
        known.Cells[number] = found;
        return found;
    }

    // the session's cell, found as the interpreter finds a variable of the session (RuntimeExpressionEvaluator.BindingOf): where the session holds it, and
    // not on the object of whichever activation is current.
    private object? SessionFieldCellOf(int number, CompiledField field)
    {
        if (_session.Storage is not SessionStorage storage)
        {
            return null;
        }

        // the cells the body found last in the same storage, while nothing was allocated, freed or rebound since (CompiledBody.SessionFields).
        var known = _body.SessionFields;
        if (known is null || known.Generation != storage.Generation || !known.Storage.TryGetTarget(out var was) || !ReferenceEquals(was, storage))
        {
            known = new SessionFieldCells(new WeakReference<SessionStorage>(storage), storage.Generation, new object?[_body.Fields.Count]);
            _body.SessionFields = known;
        }

        if (known.Cells[number] is { } cached)
        {
            return cached;
        }

        var found = _session.Symbols is SessionSymbols symbols && symbols.TryGetSessionCell(field.Symbol, out var held) ? CellOfKind(field, held) : null;
        known.Cells[number] = found;
        return found;
    }
}
