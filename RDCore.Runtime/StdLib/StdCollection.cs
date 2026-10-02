using System.Runtime.CompilerServices;
using RDCore.SDK.Model.Errors;
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
/// 🚧 TODO a collection does not count the objects it holds. An object that is held only by a collection is not kept alive by it, and one that was held by it
/// is not let go of when it is removed: the same gap <c>ObjectReferences</c> documents for an array element and a field.
/// </para>
/// </remarks>
/// <param name="session">The session the collections are objects of.</param>
/// <param name="enumerators">Makes the object a <c>For Each</c> over a collection is driven by.</param>
internal sealed class StdCollection(IRuntimeSession session, StdEnumVariant enumerators) : IStdCollectionClass, IStdLibReceiverBound
{
    private sealed record Member(string? Key, VBTypedValue Value);

    private sealed class Members
    {
        public List<Member> Items { get; } = [];
    }

    private readonly ConditionalWeakTable<IObjectInstance, Members> _members = [];

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

        members.Items.Insert(insertAt, new Member(keyText, value));
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

        members.Items.RemoveAt(position);
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
