using RDCore.Runtime.Execution;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a Set statement.
public sealed partial class CompiledActivation
{
    /// <summary>
    /// <c>Set</c> of the object variable in <paramref name="slot"/>, held in <paramref name="target"/>, to the object held in <paramref name="source"/>, or to
    /// <c>Nothing</c> (MS-VBAL §5.4.3.9): as <c>ExecuteSetAssignment</c> assigns it, the value Set-coerced to the variable's declared type, stored, then
    /// counted, and what the variable held before let go of (<c>ObjectReferences.Rebind</c>).
    /// </summary>
    /// <returns>
    /// <see langword="false"/> when the interpreter runs the statement: a cell compiled code does not read, a value that does not Set-coerce (the
    /// interpreter raises its error), or an object the variable held that letting go of would destroy, whose <c>Terminate</c> is the interpreter's to run
    /// (MS-VBAL §5.3.1.10).
    /// </returns>
    public bool TrySet(int offset, int slot, object? target, object? source, bool nothing)
        => !FailsAccessAtRandom() && target is ValueBindingHandle holder && TrySetIn(offset, holder, ((ITypedSymbol)_body.SlotSymbols[slot]).ResolvedType, source, nothing);

    /// <summary>
    /// <c>Set</c> of the object field at <paramref name="index"/> of <paramref name="record"/> to the object held in <paramref name="source"/>, or to
    /// <c>Nothing</c> (MS-VBAL §5.4.3.9): as <c>LetAssignmentEvaluator.SetField</c> assigns it, the value Set-coerced to the field's declared type, stored in the
    /// field's cell, then counted there, and what the field held before let go of.
    /// </summary>
    /// <returns><see langword="false"/> when the interpreter runs the statement, as for <see cref="TrySet"/>, or when there is no record or no such field.</returns>
    public bool TrySetField(int offset, object? record, int index, object? source, bool nothing)
        => !FailsAccessAtRandom() && record is VBUserDefinedTypeValue held && index < held.Fields.Length && held.Fields[index].ResolvedType is { } declared
            && RecordOperations.ObjectField(held, index) is ValueBindingHandle cell && TrySetIn(offset, cell, declared, source, nothing);

    // the Set at offset of what holder holds to source's object, or Nothing, Set-coerced to declared.
    private bool TrySetIn(int offset, ValueBindingHandle holder, VBType declared, object? source, bool nothing)
        => HoldsAlready(holder, source, nothing)
            || ValueOf(source, nothing) is { } value && Coerced(offset, value, declared) is { } coerced && Assign(holder, coerced);

    // whether holder holds the object Set to it already, or Nothing when Nothing is: the Set stores what is there, counts it there and lets it go there, which
    // leaves the cell and the object as they were. The object was Set-coerced to the cell's type when it was stored.
    private static bool HoldsAlready(ValueBindingHandle holder, object? source, bool nothing)
        => holder is { IsKnown: true, Value: VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var held } }
            && (nothing ? held.Equals(VBObjectValue.Nothing.Value) : ObjectOperations.TryRead(source, out var instance) && held.Equals(instance));

    // a Set-coerced value stored in holder, counted there, and what holder held before let go of (ObjectReferences.Rebind); false, with nothing stored, when
    // letting go of what it held would destroy an object.
    private bool Assign(ValueBindingHandle holder, VBTypedValue coerced)
    {
        if (holder.Value is VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var held } && coerced is VBObjectValue { Value: var instance })
        {
            return Rebind(holder, held, instance, coerced);
        }

        var before = ObjectReferences.HeldIn(_session, holder);
        if (Destroys(before))
        {
            return false;
        }

        holder.Store(_session.Symbols.Resolver, coerced);
        ObjectReferences.Rebind(_session, holder, before);
        return true;
    }

    // an object variable's cell, which holds one reference and nothing else: the object Set to it is counted there, and the one it held let go of there, as
    // ObjectReferences.Rebind counts them, without walking what the cell holds; false, with nothing stored, when letting go of the one it held would destroy it.
    private bool Rebind(ValueBindingHandle holder, VBRuntimeObjectId held, VBRuntimeObjectId instance, VBTypedValue coerced)
    {
        var heldHere = !held.Equals(VBObjectValue.Nothing.Value) && _session.Objects.IsHeldBy(held, holder);
        if (heldHere && _session.Objects.RefCount(held) <= 1 && !DestroysWithoutCode(held))
        {
            return false;
        }

        holder.Store(_session.Symbols.Resolver, coerced);
        if (!instance.Equals(VBObjectValue.Nothing.Value))
        {
            _session.Objects.AddRef(instance, holder);
        }

        if (heldHere)
        {
            _session.ReleaseReference(held, holder);
        }

        return true;
    }

    /// <summary>
    /// <c>Set</c> of the function result variable to the object held in <paramref name="source"/>, or to <c>Nothing</c> (MS-VBAL §5.3.1, §5.4.3.9): as
    /// <c>ExecuteSetAssignment</c> assigns it, in the frame's result cell (<c>ObjectReferences.AssignResult</c>), which holds it for as long as the activation
    /// runs.
    /// </summary>
    /// <returns><see langword="false"/> when the interpreter runs the statement, as <see cref="TrySet"/>.</returns>
    public bool TrySetResult(int offset, object? source, bool nothing)
    {
        if (FailsAccessAtRandom() || Frame is not { Procedure: ITypedSymbol function } frame
            || ValueOf(source, nothing) is not { } value
            || Coerced(offset, value, function.ResolvedType) is not { } coerced
            || Destroys(ObjectReferences.HeldIn(_session, frame.ResultCell)))
        {
            return false;
        }

        frame.ReturnValue = ObjectReferences.AssignResult(_session, frame.ResultCell, coerced);
        return true;
    }

    // the object held in source, which is a copy of its reference and not a view of the cell, or Nothing; none when the cell holds no reference compiled
    // code reads.
    private static VBObjectValue? ValueOf(object? source, bool nothing)
        => nothing ? VBObjectValue.Nothing : ObjectOperations.TryRead(source, out var instance) ? new VBObjectValue(instance) : null;

    // the value of the Set at offset Set-coerced to declared (MS-VBAL §5.5.2.2); none when it does not Set-coerce.
    private VBTypedValue? Coerced(int offset, VBObjectValue value, VBType declared)
        => _executor.Expressions.SetCoercion is { } coercion
            && coercion.EvaluateSetCoercion(_session, ((AssignmentStatementNode)_list.Items[offset].Node!).Value, value, declared) is { IsSuccess: true } coerced
                ? coerced.Result
                : null;

    // whether letting go of what a cell held destroys an object that runs code as it goes: the cell is the last that holds it.
    private bool Destroys(ObjectReferences.Held held)
    {
        foreach (var (cell, instance) in held)
        {
            if (_session.Objects.IsHeldBy(instance, cell) && _session.Objects.RefCount(instance) <= 1 && !DestroysWithoutCode(instance))
            {
                return true;
            }
        }

        return false;
    }

    // whether an object that loses its last reference goes without running any code, so that compiled code lets go of it, and the session counts and
    // destroys it as it does for the interpreter (RuntimeSession.ReleaseReference). Its class is one of the workspace's (a library's, such as a Collection,
    // lets go of what it holds), and is no form, with no Terminate handler. None of its variables or Static locals can hold an object, which would be let go
    // of with it. Nothing outside the workspace holds it.
    private bool DestroysWithoutCode(VBRuntimeObjectId instance)
        => _session.Symbols.TryGetInstance(instance, out var live) && live is ObjectInstance created
            && created.ClassModule.GetProperty(SymbolProperties.Library) is null && created.ClassModule.GetProperty(SymbolProperties.ExternalTarget) is null
            && (_session.Lifecycle is null or ClassLifecycle) && !ClassLifecycle.HandlesTerminate(created)
            && !_session.ExternalObjects.TryGet(instance, out _, out _)
            && created.Fields.All(HoldsNoObject) && created.StaticLocals.All(HoldsNoObject);

    private static bool HoldsNoObject(Symbol variable) => variable is ITypedSymbol { ResolvedType: { } type } && HoldsNoObject(type);

    // a number, a String, a Boolean or a Date, or an array or a record made of them only: a Variant, an object and anything else may hold an object.
    private static bool HoldsNoObject(VBType type) => type switch
    {
        VBNumericType or VBStringType or VBBooleanType or VBDateType => true,
        VBArrayType { ItemType: var item } => HoldsNoObject(item),
        VBUserDefinedType record => record.Fields().All(HoldsNoObject),
        _ => false,
    };
}
