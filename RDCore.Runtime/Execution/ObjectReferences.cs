using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Keeps the reference counts of the session's objects (<see cref="ISessionObjects"/>) in step with what holds them, which is what makes an
/// object's <c>Terminate</c> run when the last thing that held it lets it go (<strong>MS-VBAL §5.3.1.10</strong>) - and never before.
/// </summary>
/// <remarks>
/// A reference is held by a <em>cell</em>, by identity: the cell of a variable, an element of an array, a field of a user-defined type or of an
/// object, a member of a collection. What a cell holds is read from the cell itself, the whole way down - an object a <c>Variant</c> holds is held
/// by the Variant's cell, and the objects the elements of an array hold, wherever the array is (a variable, a Variant, a field), are held each by
/// its element's cell. Every write to a cell is made between <see cref="HeldIn"/> and <see cref="Rebind"/>: what the cell holds after the write
/// is counted, then what it held before is let go of, in that order so that a cell given the object it already held does not destroy it on the
/// way. A cell that ceases to exist - a local at the end of its activation, a field of an object that is destroyed, a member a collection
/// removes - is let go of with <see cref="Release(IRuntimeSession, Held)"/>.
/// <para>
/// This is sound only because no two holders share a cell: an assignment copies an array or a record into cells of its own
/// (<see cref="DataValues"/>). A reference held by something this does not count is never released by it; it is the counted ones that keep
/// an object alive, and an object one of them does not see could be destroyed while that one still holds it.
/// </para>
/// <para>
/// The value of an expression is a holder too, for as long as the statement that evaluates it runs (<see cref="HoldTemporarily"/>): the object
/// <c>New</c> made, what a call returned - taken from the function result variable (<see cref="AssignResult"/>) before the function's locals let go
/// of what they held. The object of a <c>With</c> block that is not a variable, and what a <c>For Each</c> enumerates, are held by the block until it
/// is over; <c>Me</c> is held by the member it is the object of, as any parameter is.
/// </para>
/// </remarks>
internal static class ObjectReferences
{
    /// <summary>
    /// What a cell holds: each object, with the cell that holds it.
    /// </summary>
    /// <remarks>
    /// A snapshot: taken before a write, it is what the cell held, whatever the write does to the cells it is made of.
    /// </remarks>
    public readonly struct Held
    {
        // a cell holds one object far more often than it holds several: the first is kept inline, and only the others need a list.
        private readonly (IBindingHandle Cell, VBRuntimeObjectId Object) _first;
        private readonly List<(IBindingHandle, VBRuntimeObjectId)>? _more;

        internal Held(Collector collected) => (Count, _first, _more) = (collected.Count, collected.First, collected.More);

        /// <summary>Nothing held.</summary>
        public static Held None => default;

        /// <summary>How many objects are held, by however many cells.</summary>
        public int Count { get; }

        internal (IBindingHandle Cell, VBRuntimeObjectId Object) this[int index] => index == 0 ? _first : _more![index - 1];

        /// <summary>Whether <paramref name="instance"/> is one of the objects held.</summary>
        public bool Contains(VBRuntimeObjectId instance)
        {
            for (var index = 0; index < Count; index++)
            {
                if (this[index].Object.Equals(instance))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Whether nothing is held.</summary>
        public bool IsEmpty => Count == 0;

        /// <summary>Enumerates each object held, with the cell that holds it - without allocating, as every write to a cell does it.</summary>
        public Enumerator GetEnumerator() => new(this);

        /// <inheritdoc cref="GetEnumerator"/>
        public struct Enumerator(Held held)
        {
            private int _index = -1;

            public bool MoveNext() => ++_index < held.Count;

            public readonly (IBindingHandle Cell, VBRuntimeObjectId Object) Current => held[_index];
        }
    }

    // what a walk of a cell finds, as it finds it.
    internal struct Collector
    {
        public int Count;
        public (IBindingHandle, VBRuntimeObjectId) First;
        public List<(IBindingHandle, VBRuntimeObjectId)>? More;

        public void Add(IBindingHandle cell, VBRuntimeObjectId instance)
        {
            if (Count++ == 0)
            {
                First = (cell, instance);
            }
            else
            {
                (More ??= []).Add((cell, instance));
            }
        }
    }

    private static readonly VBRuntimeObjectId NothingId = VBObjectValue.Nothing.Value;

    /// <summary>
    /// Holds <paramref name="value"/> as a temporary of the statement being executed (<see cref="ISessionObjects.PushTemporary"/>): the object
    /// <c>New</c> made, what a call returned. The statement's own value keeps what it references alive until the statement is over, and lets go of
    /// it then (<see cref="ReleaseTemporaries"/>) - after it was assigned, if it was, so that whatever it was assigned to holds it by then.
    /// </summary>
    /// <param name="session">The session whose objects they are.</param>
    /// <param name="value">The value; one that holds no object is not held.</param>
    public static void HoldTemporarily(IRuntimeSession session, VBTypedValue? value)
    {
        if (value is not (VBObjectValue or VBVariantValue or VBArrayValue or VBUserDefinedTypeValue))
        {
            return;
        }

        // a cell of its own, holding the value as it is now: the value may be a view of a variable's cell, which the statement may assign next.
        var cell = BindingKnowledge.NewCell(Boxed(value), !value.IsIndeterminate);
        var held = HeldIn(session, cell);
        if (held.IsEmpty)
        {
            return;
        }

        session.Objects.PushTemporary(cell);
        foreach (var (holder, instance) in held)
        {
            session.Objects.AddRef(instance, holder);
        }
    }

    /// <summary>
    /// Lets go of the temporaries held since <paramref name="mark"/> (<see cref="ISessionObjects.TemporaryCount"/>, read before the statement that
    /// made them): the statement is over.
    /// </summary>
    public static void ReleaseTemporaries(IRuntimeSession session, int mark)
    {
        while (session.Objects.TemporaryCount > mark && session.Objects.PopTemporary() is { } cell)
        {
            Release(session, cell);
        }
    }

    /// <summary>
    /// Assigns a function's result (<strong>MS-VBAL §5.3.1</strong>: "a distinct function result variable"), which holds what it is assigned as a
    /// variable does: in a cell of its own, until the function returns it.
    /// </summary>
    /// <param name="session">The session whose objects they are.</param>
    /// <param name="resultCell">The cell of the function result variable.</param>
    /// <param name="value">The value assigned, coerced to the result's type.</param>
    /// <returns>The value the result now is: a copy, not a view of the variable it may have been read from.</returns>
    public static VBTypedValue AssignResult(IRuntimeSession session, IKnowledgeBinding resultCell, VBTypedValue value)
    {
        if (value is not (VBObjectValue or VBVariantValue or VBArrayValue or VBUserDefinedTypeValue))
        {
            return value;
        }

        var stored = value is VBObjectValue or VBVariantValue ? DataValues.Copy(value) : value;
        var before = HeldIn(session, resultCell);
        resultCell.Store(Boxed(stored), !stored.IsIndeterminate);
        Rebind(session, resultCell, before);
        return stored;
    }

    /// <summary>
    /// Holds a copy of <paramref name="value"/> in a new cell of its own, which holds the objects in it: what a library object keeps that is no
    /// variable - the member of a collection, the members an enumerator enumerates. The cell is let go of with
    /// <see cref="Release(IRuntimeSession, IBindingHandle, Held)"/> when the object no longer keeps it.
    /// </summary>
    /// <returns>The copy, and the cell that holds it.</returns>
    public static (VBTypedValue Value, ValueBindingHandle Cell) Keep(IRuntimeSession session, VBTypedValue value)
    {
        var copied = DataValues.Copy(value);
        var cell = BindingKnowledge.NewCell(Boxed(copied), !copied.IsIndeterminate);
        Rebind(session, cell, Held.None);
        return (copied, cell);
    }

    // how a cell stores a value: an array or a record boxed, as a variable stores one (SymbolAddressTable.BoxedValue).
    private static IRuntimeValue Boxed(VBTypedValue value)
        => value is VBArrayValue or VBUserDefinedTypeValue ? SymbolAddressTable.BoxedValue(value) : value.RuntimeValue;

    /// <summary>
    /// Reads what <paramref name="holder"/> holds, before it is written to.
    /// </summary>
    /// <param name="session">The session whose objects they are.</param>
    /// <param name="holder">The cell about to be written: a variable's, an element's or a field's. A cell that aliases another
    /// (a <c>ByRef</c> parameter's) is the cell it aliases.</param>
    public static Held HeldIn(IRuntimeSession session, IBindingHandle holder)
    {
        var holds = default(Collector);
        if (CellOf(session, holder) is { } cell)
        {
            Collect(cell, cell.Value, ref holds);
        }

        return new Held(holds);
    }

    /// <summary>
    /// Reads what a value holds, as it is held in <paramref name="cell"/> - for a value that is not read back from its cell: a function's result.
    /// </summary>
    public static Held HeldBy(IBindingHandle cell, VBTypedValue? value)
    {
        var holds = default(Collector);
        if (value is not null)
        {
            CollectTyped(cell, value, ref holds);
        }

        return new Held(holds);
    }

    /// <summary>
    /// Records what <paramref name="holder"/> holds now that it was written to, and lets go of what it held before.
    /// </summary>
    /// <param name="session">The session whose objects they are.</param>
    /// <param name="holder">The cell that was written.</param>
    /// <param name="before">What it held, read with <see cref="HeldIn"/> before the write.</param>
    public static void Rebind(IRuntimeSession session, IBindingHandle holder, Held before)
    {
        foreach (var (cell, instance) in HeldIn(session, holder))
        {
            session.Objects.AddRef(instance, cell);
        }

        Release(session, before);
    }

    /// <summary>
    /// Lets go of what <paramref name="held"/> says was held, because the cells that held it ceased to exist, or now hold something else.
    /// </summary>
    /// <param name="session">The session whose objects they are.</param>
    /// <param name="held">What was held, by which cell.</param>
    /// <param name="kept">Objects that outlive the cells because something else is taking them: what a function returns.</param>
    public static void Release(IRuntimeSession session, Held held, Held kept = default)
    {
        foreach (var (cell, instance) in held)
        {
            if (!kept.Contains(instance) && session.Objects.IsHeldBy(instance, cell))
            {
                session.ReleaseReference(instance, cell);
            }
        }
    }

    /// <summary>
    /// Lets go of what <paramref name="holder"/> holds, because it ceases to exist.
    /// </summary>
    public static void Release(IRuntimeSession session, IBindingHandle holder, Held kept = default)
        => Release(session, HeldIn(session, holder), kept);

    // the cell a write lands in: a ByRef parameter's binding is the address of the variable it was given, which holds what is written through it.
    private static IBindingHandle? CellOf(IRuntimeSession session, IBindingHandle holder)
    {
        var cell = holder;
        var hops = 0;
        while (cell is ReferenceBindingHandle { Value: VBRuntimeReference reference })
        {
            if (++hops > 64 || !session.Symbols.Resolver.TryRead(reference.Value, out var bound) || bound is null)
            {
                return null;
            }

            cell = bound;
        }

        return cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue) ? cell : null;
    }

    // what is stored in a cell: an object's reference, or a box - a Variant, an array, a record - with what it holds in it.
    private static void Collect(IBindingHandle cell, IRuntimeValue value, ref Collector holds)
    {
        switch (value)
        {
            case VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var instance }:
                Add(cell, instance, ref holds);
                break;
            case VBRuntimeVariantValue { WrappedValue: var held }:
                CollectTyped(cell, held, ref holds);
                break;
            case VBRuntimeValue<VBRuntimeArrayValue> { StoredValue: var boxed }:
                CollectArray(boxed.Array, ref holds);
                break;
            case VBRuntimeValue<VBRuntimeUserDefinedTypeValue> { StoredValue: var boxed }:
                CollectRecord(boxed.UserDefinedType, ref holds);
                break;
        }
    }

    private static void CollectTyped(IBindingHandle cell, VBTypedValue value, ref Collector holds)
    {
        switch (value)
        {
            case VBObjectValue { RuntimeValue: VBRuntimeValue<VBRuntimeObjectId> { StoredValue: var instance } }:
                Add(cell, instance, ref holds);
                break;
            case VBVariantValue variant:
                CollectTyped(cell, variant.TypedValue, ref holds);
                break;
            case VBArrayValue array:
                CollectArray(array, ref holds);
                break;
            case VBUserDefinedTypeValue record:
                CollectRecord(record, ref holds);
                break;
        }
    }

    private static void CollectArray(VBArrayValue array, ref Collector holds)
    {
        // an array of numbers, strings or dates holds no object: there is nothing to walk.
        if (!CanHoldAnObject(array.ItemType))
        {
            return;
        }

        for (var index = 0; index < array.Length; index++)
        {
            if (array.CellAt(index) is { } cell && cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue))
            {
                Collect(cell, cell.Value, ref holds);
            }
        }
    }

    /// <summary>
    /// Reads what a field of a record holds, before the field is replaced (<see cref="VBUserDefinedTypeValue.TrySetFieldAt"/>): a field that is
    /// replaced, rather than written through its cell, is given a cell of its own.
    /// </summary>
    /// <param name="record">The record.</param>
    /// <param name="index">The field's position in declaration order.</param>
    public static Held HeldInField(VBUserDefinedTypeValue record, int index)
    {
        var holds = default(Collector);
        CollectField(record.StoredFieldAt(index), ref holds);
        return new Held(holds);
    }

    /// <summary>
    /// Records what a field of a record holds now that it was replaced, and lets go of what it held before.
    /// </summary>
    public static void RebindField(IRuntimeSession session, VBUserDefinedTypeValue record, int index, Held before)
    {
        foreach (var (cell, instance) in HeldInField(record, index))
        {
            session.Objects.AddRef(instance, cell);
        }

        Release(session, before);
    }

    private static void CollectRecord(VBUserDefinedTypeValue record, ref Collector holds)
    {
        for (var index = 0; index < record.FieldCount; index++)
        {
            CollectField(record.StoredFieldAt(index), ref holds);
        }
    }

    private static void CollectField(VBTypedValue? field, ref Collector holds)
    {
        switch (field)
        {
            // an array or a record is the field itself, made of cells of its own.
            case VBArrayValue array:
                CollectArray(array, ref holds);
                break;
            case VBUserDefinedTypeValue nested:
                CollectRecord(nested, ref holds);
                break;
            // anything else is in the field's cell, which a write to the field writes to: what it holds now is read there.
            case { Handle: var cell } when cell.BindingCapabilities.HasFlag(BindingCapabilities.GetValue):
                Collect(cell, cell.Value, ref holds);
                break;
        }
    }

    /// <summary>
    /// Whether a cell of <paramref name="type"/> can hold an object, directly or in what it is made of: whether a write to one needs to be counted.
    /// </summary>
    public static bool CanHoldAnObject(VBType? type)
        => type is VBVariantType or VBObjectType or VBClassType or VBUserDefinedType or VBArrayType;

    private static void Add(IBindingHandle cell, VBRuntimeObjectId instance, ref Collector holds)
    {
        if (!instance.Equals(NothingId))
        {
            holds.Add(cell, instance);
        }
    }
}
