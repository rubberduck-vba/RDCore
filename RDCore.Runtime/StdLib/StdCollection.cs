using System.Runtime.CompilerServices;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <inheritdoc cref="IStdCollectionClass"/>
/// <remarks>
/// The members of a collection are state of the object, so they are kept with its own instance and last as long as it does. Each is a value with the key it was
/// added with, if it was: a key is a <c>String</c> that is the same key whatever the case of its letters.
/// <para>
/// A member holds the objects in it as a variable does, by a cell of its own (<see cref="ObjectReferences"/>): an object a collection holds is kept alive by
/// it, and let go of when the member is removed or the collection is destroyed.
/// </para>
/// </remarks>
internal sealed class StdCollection : IStdCollectionClass, IStdLibReceiverBound
{
    // the value is what the member is; the cell holds it, and is what the objects in it are held by.
    private sealed record Member(string? Key, VBTypedValue Value, IBindingHandle Cell);

    private sealed class Members
    {
        public List<Member> Items { get; } = [];
    }

    private readonly IRuntimeSession session;
    private readonly StdEnumVariant enumerators;
    private readonly ConditionalWeakTable<IObjectInstance, Members> _members = [];

    /// <param name="session">The session the collections are objects of.</param>
    /// <param name="enumerators">Makes the object a <c>For Each</c> over a collection is driven by.</param>
    public StdCollection(IRuntimeSession session, StdEnumVariant enumerators)
    {
        this.session = session;
        this.enumerators = enumerators;
        session.Objects.OnDestroying(ReleaseMembersOf);
    }

    // MS-VBAL §5.3.1.10: a collection that is destroyed lets go of what its members hold, which can be what destroys those in turn.
    private void ReleaseMembersOf(VBRuntimeObjectId destroyed)
    {
        if (!session.Symbols.TryGetInstance(destroyed, out var instance) || !_members.TryGetValue(instance, out var members))
        {
            return;
        }

        var released = members.Items.ToArray();
        members.Items.Clear();
        foreach (var member in released)
        {
            ObjectReferences.Release(session, member.Cell);
        }
    }

    /// <inheritdoc/>
    public VBRuntimeObjectId? Receiver { get; set; }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Count()
        => TryGetMembers(out var members)
            ? RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(members.Items.Count))
            : RuntimeSemanticsEvaluationResult<VBLongValue>.InternalError();

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Item(VBVariantValue index)
    {
        if (!TryGetMembers(out var members))
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.InternalError();
        }

        return Locate(members, index, out var position) is { } error
            ? RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(error)
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(new VBVariantValue(members.Items[position].Value));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Add(VBVariantValue item, VBVariantValue? key = default, VBVariantValue? before = default, VBVariantValue? after = default)
    {
        if (!TryGetMembers(out var members))
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        var value = Unwrapped(item);
        if (value is VBUserDefinedTypeValue)
        {
            // "Only user-defined types defined in public object modules can be coerced to or from a variant": the one case of a member that is no value.
            return Fail(VBRuntimeErrorId.TypeMismatch, "A user-defined type cannot be a member of a collection.");
        }

        string? keyText = null;
        if (Given(key) is { } given)
        {
            if (given is not VBStringValue text)
            {
                return Fail(VBRuntimeErrorId.TypeMismatch, "The key of a member is a String.");
            }

            keyText = text.Value;
            if (members.Items.Any(member => IsKey(member, keyText)))
            {
                return Fail(VBRuntimeErrorId.KeyAlreadyAssociatedWithAnElementOfCollection, $"The key '{keyText}' is already in use.");
            }
        }

        // "Either a Before position or an After position can be specified, but not both."
        if (Given(before) is not null && Given(after) is not null)
        {
            return Fail(VBRuntimeErrorId.InvalidProcedureCallOrArgument, "Before and After cannot both be given.");
        }

        var insertAt = members.Items.Count;
        if (Given(before) is not null)
        {
            if (Locate(members, before!, out insertAt) is { } error)
            {
                return RuntimeSemanticsEvaluationResult.Error(error);
            }
        }
        else if (Given(after) is not null)
        {
            if (Locate(members, after!, out insertAt) is { } error)
            {
                return RuntimeSemanticsEvaluationResult.Error(error);
            }

            insertAt++;
        }

        // a member is the collection's own copy of what it was given, held in a cell of its own as an element is (an array is stored boxed).
        var copied = DataValues.Copy(value);
        var cell = BindingKnowledge.NewCell(copied is VBArrayValue ? SymbolAddressTable.BoxedValue(copied) : copied.RuntimeValue, !copied.IsIndeterminate);
        members.Items.Insert(insertAt, new Member(keyText, copied, cell));
        ObjectReferences.Rebind(session, cell, ObjectReferences.Held.None);
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult Remove(VBVariantValue index)
    {
        if (!TryGetMembers(out var members))
        {
            return RuntimeSemanticsEvaluationResult.InternalError();
        }

        if (Locate(members, index, out var position) is { } error)
        {
            return RuntimeSemanticsEvaluationResult.Error(error);
        }

        var removed = members.Items[position];
        members.Items.RemoveAt(position);
        ObjectReferences.Release(session, removed.Cell);
        return RuntimeSemanticsEvaluationResult.Success(VBVoidValue.Void);
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum()
    {
        if (!TryGetMembers(out var members))
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
        }

        return enumerators.Create([.. members.Items.Select(member => member.Value)]) is { } enumerator
            ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(enumerator)
            : RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
    }

    // "If a numeric expression, Index MUST be a number from 1 to the value of the collection's Count property. If a string expression, Index MUST
    // correspond to the Key argument specified when the member referred to was added to the collection." A position that is not there is a subscript out
    // of range, as it is for an array; a key that is not there is not a subscript at all, and is an invalid argument.
    private static VBRuntimeErrorInfo? Locate(Members members, VBVariantValue index, out int position)
    {
        position = -1;
        switch (Unwrapped(index))
        {
            case VBStringValue text:
                var key = text.Value;
                position = members.Items.FindIndex(member => IsKey(member, key));
                return position >= 0 ? null : Error(VBRuntimeErrorId.InvalidProcedureCallOrArgument, $"There is no member with the key '{key}'.");

            case VBNumericTypedValue number:
                var ordinal = (long)Math.Round(number.AsDouble, MidpointRounding.ToEven);
                position = (int)(ordinal - 1);
                return ordinal >= 1 && ordinal <= members.Items.Count
                    ? null
                    : Error(VBRuntimeErrorId.SubscriptOutOfRange, $"There is no member at position {ordinal}, in a collection of {members.Items.Count}.");

            default:
                return Error(VBRuntimeErrorId.TypeMismatch, "A member is reached by its position, or by its key.");
        }
    }

    private static bool IsKey(Member member, string key) => member.Key is { } own && own.Equals(key, StringComparison.OrdinalIgnoreCase);

    // an optional argument that is not there arrives as nothing, or as the Empty an omitted Variant is.
    private static VBTypedValue? Given(VBVariantValue? argument)
        => argument is null ? null : Unwrapped(argument) is VBEmptyValue ? null : Unwrapped(argument);

    private static VBTypedValue Unwrapped(VBTypedValue value)
    {
        while (value is VBVariantValue { TypedValue: { } wrapped })
        {
            value = wrapped;
        }

        return value;
    }

    private bool TryGetMembers(out Members members)
    {
        members = null!;
        return Receiver is { } receiver
            && session.Symbols.TryGetInstance(receiver, out var instance)
            && (_members.TryGetValue(instance, out members!) || TryCreate(instance, out members));
    }

    private bool TryCreate(IObjectInstance instance, out Members members)
    {
        members = _members.GetValue(instance, _ => new Members());
        return true;
    }

    private static RuntimeSemanticsEvaluationResult Fail(VBRuntimeErrorId id, string verbose)
        => RuntimeSemanticsEvaluationResult.Error(Error(id, verbose));

    private static VBRuntimeErrorInfo Error(VBRuntimeErrorId id, string verbose) => VBRuntimeErrorInfo.For(id, default, verbose);
}
