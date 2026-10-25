using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using RDCore.Runtime.Execution;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Model.Values.Runtime;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Runtime.StdLib;

/// <summary>
/// One match of a pattern in a text, as <c>RegExp.Execute</c> found it: the text it matched, where, and what each group matched - <see langword="null"/>
/// for a group that took no part in it.
/// </summary>
internal sealed record class MatchData(string Value, int FirstIndex, string?[] Groups)
{
    /// <summary>
    /// The match that <paramref name="found"/> is, in <paramref name="text"/>: what was matched against may be the text in lower case, and what a match is, is
    /// the text's.
    /// </summary>
    public static MatchData Of(Match found, string text, int groupCount)
    {
        var groups = new string?[groupCount];
        for (var group = 1; group <= groupCount; group++)
        {
            var captured = found.Groups[group];
            groups[group - 1] = captured.Success ? text.Substring(captured.Index, captured.Length) : null;
        }

        return new(text.Substring(found.Index, found.Length), found.Index, groups);
    }
}

/// <summary>
/// Makes an object of a class of the standard library that only another one makes: a <c>MatchCollection</c>, a <c>Match</c>, a <c>SubMatches</c>.
/// </summary>
internal static class StdLibObjects
{
    /// <summary>
    /// A new object of the class of the standard library named <paramref name="className"/>, and its instance, or <see langword="null"/> when the library has
    /// no such class.
    /// </summary>
    public static (VBObjectValue Object, IObjectInstance Instance)? New(IRuntimeSession session, string className)
    {
        if (VBProjectSymbol.ResolveQualifiedType(session.Symbols.Resolver, StdLibSymbolProvider.LibraryName, className, StaticSymbol.GlobalUri).Symbol
            is not VBClassModuleSymbol classModule)
        {
            return null;
        }

        var objectId = session.Objects.CreateObject();
        return (new VBObjectValue(objectId), session.Symbols.CreateInstance(objectId, classModule));
    }

    /// <summary>
    /// The error of an index that is no position of a collection numbered from <c>0</c>.
    /// </summary>
    public static IVBRaisableError OutOfRange(int index, int count)
        => VBRuntimeErrorInfo.For(VBRuntimeErrorId.InvalidProcedureCallOrArgument, default,
            count == 0 ? $"There is no member {index}: the collection is empty." : $"There is no member {index}: the members are numbered from 0 to {count - 1}.");
}

/// <inheritdoc cref="IStdMatchCollectionClass"/>
/// <remarks>
/// A collection is the matches it was made with, which do not change. Each <c>Match</c> is made the first time it is asked for, and is the same object every time
/// after: the collection holds it, as a collection's members are held (<see cref="ObjectReferences.Keep"/>), and lets go of it when it is destroyed itself.
/// </remarks>
internal sealed class StdMatchCollection : IStdMatchCollectionClass, IStdLibReceiverBound
{
    private sealed class Matches(IReadOnlyList<MatchData> found)
    {
        public IReadOnlyList<MatchData> Found { get; } = found;

        // the objects made for them so far, and the cells that hold them.
        public (VBTypedValue Value, IBindingHandle Cell)?[] Objects { get; } = new (VBTypedValue, IBindingHandle)?[found.Count];
    }

    private readonly IRuntimeSession session;
    private readonly StdMatch match;
    private readonly StdEnumVariant enumerators;
    private readonly ConditionalWeakTable<IObjectInstance, Matches> _matches = [];

    /// <param name="session">The session the objects are objects of.</param>
    /// <param name="match">Makes the <c>Match</c> of each match.</param>
    /// <param name="enumerators">Makes the object a <c>For Each</c> over the matches is driven by.</param>
    public StdMatchCollection(IRuntimeSession session, StdMatch match, StdEnumVariant enumerators)
    {
        this.session = session;
        this.match = match;
        this.enumerators = enumerators;
        session.Objects.OnDestroying(ReleaseMatchesOf);
    }

    // MS-VBAL §5.3.1.10: a collection that is destroyed lets go of the matches it holds.
    private void ReleaseMatchesOf(VBRuntimeObjectId destroyed)
    {
        if (!session.Symbols.TryGetInstance(destroyed, out var instance) || !_matches.TryGetValue(instance, out var matches))
        {
            return;
        }

        _matches.Remove(instance);
        foreach (var held in matches.Objects)
        {
            if (held is { Cell: var cell })
            {
                ObjectReferences.Release(session, cell);
            }
        }
    }

    /// <inheritdoc/>
    public VBRuntimeObjectId? Receiver { get; set; }

    /// <summary>
    /// Makes a collection of <paramref name="found"/>, or <see langword="null"/> when the library has no such class.
    /// </summary>
    public VBObjectValue? Create(IReadOnlyList<MatchData> found)
    {
        if (StdLibObjects.New(session, "MatchCollection") is not { } made)
        {
            return null;
        }

        _matches.Add(made.Instance, new Matches(found));
        return made.Object;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Count()
        => TryGetMatches(out var matches)
            ? RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(matches.Found.Count))
            : RuntimeSemanticsEvaluationResult<VBLongValue>.InternalError();

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> Item(VBLongValue index)
    {
        if (!TryGetMatches(out var matches))
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
        }

        var position = index.Value;
        if (position < 0 || position >= matches.Found.Count)
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.Error(StdLibObjects.OutOfRange(position, matches.Found.Count));
        }

        return ObjectAt(matches, position) is { } made
            ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(made)
            : RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum()
    {
        if (!TryGetMatches(out var matches))
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
        }

        var members = new List<VBTypedValue>();
        for (var position = 0; position < matches.Found.Count; position++)
        {
            if (ObjectAt(matches, position) is not { } made)
            {
                return RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
            }

            members.Add(made);
        }

        return enumerators.Create(members) is { } enumerator
            ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(enumerator)
            : RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
    }

    private VBObjectValue? ObjectAt(Matches matches, int position)
    {
        if (matches.Objects[position] is { Value: VBObjectValue held })
        {
            return held;
        }

        if (match.Create(matches.Found[position]) is not { } made)
        {
            return null;
        }

        var (kept, cell) = ObjectReferences.Keep(session, made);
        matches.Objects[position] = (kept, cell);
        return (VBObjectValue)kept;
    }

    private bool TryGetMatches(out Matches matches)
    {
        matches = null!;
        return Receiver is { } receiver
            && session.Symbols.TryGetInstance(receiver, out var instance)
            && _matches.TryGetValue(instance, out matches!);
    }
}

/// <inheritdoc cref="IStdMatchClass"/>
/// <remarks>
/// A match is what it was made with. Its <c>SubMatches</c> is made the first time it is asked for, and is the same object every time after, which the match
/// holds and lets go of when it is destroyed itself.
/// </remarks>
internal sealed class StdMatch : IStdMatchClass, IStdLibReceiverBound
{
    private sealed class Found(MatchData data)
    {
        public MatchData Data { get; } = data;

        public (VBTypedValue Value, IBindingHandle Cell)? SubMatches { get; set; }
    }

    private readonly IRuntimeSession session;
    private readonly StdSubMatches subMatches;
    private readonly ConditionalWeakTable<IObjectInstance, Found> _found = [];

    /// <param name="session">The session the objects are objects of.</param>
    /// <param name="subMatches">Makes what the groups of a match matched.</param>
    public StdMatch(IRuntimeSession session, StdSubMatches subMatches)
    {
        this.session = session;
        this.subMatches = subMatches;
        session.Objects.OnDestroying(ReleaseSubMatchesOf);
    }

    private void ReleaseSubMatchesOf(VBRuntimeObjectId destroyed)
    {
        if (!session.Symbols.TryGetInstance(destroyed, out var instance) || !_found.TryGetValue(instance, out var found))
        {
            return;
        }

        _found.Remove(instance);
        if (found.SubMatches is { Cell: var cell })
        {
            ObjectReferences.Release(session, cell);
        }
    }

    /// <inheritdoc/>
    public VBRuntimeObjectId? Receiver { get; set; }

    /// <summary>
    /// Makes the match of <paramref name="data"/>, or <see langword="null"/> when the library has no such class.
    /// </summary>
    public VBObjectValue? Create(MatchData data)
    {
        if (StdLibObjects.New(session, "Match") is not { } made)
        {
            return null;
        }

        _found.Add(made.Instance, new Found(data));
        return made.Object;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBStringValue> Value() => Read(found => new VBStringValue(found.Data.Value));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> FirstIndex() => Read(found => new VBLongValue(found.Data.FirstIndex));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Length() => Read(found => new VBLongValue(found.Data.Value.Length));

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> SubMatches()
    {
        if (!TryGetFound(out var found))
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
        }

        if (found.SubMatches is { Value: VBObjectValue held })
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(held);
        }

        if (subMatches.Create(found.Data.Groups) is not { } made)
        {
            return RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();
        }

        var (kept, cell) = ObjectReferences.Keep(session, made);
        found.SubMatches = (kept, cell);
        return RuntimeSemanticsEvaluationResult<VBObjectValue>.Success((VBObjectValue)kept);
    }

    private RuntimeSemanticsEvaluationResult<T> Read<T>(Func<Found, T> read) where T : VBTypedValue
        => TryGetFound(out var found)
            ? RuntimeSemanticsEvaluationResult<T>.Success(read(found))
            : RuntimeSemanticsEvaluationResult<T>.InternalError();

    private bool TryGetFound(out Found found)
    {
        found = null!;
        return Receiver is { } receiver
            && session.Symbols.TryGetInstance(receiver, out var instance)
            && _found.TryGetValue(instance, out found!);
    }
}

/// <inheritdoc cref="IStdSubMatchesClass"/>
internal sealed class StdSubMatches : IStdSubMatchesClass, IStdLibReceiverBound
{
    private sealed class Groups(string?[] matched)
    {
        public string?[] Matched { get; } = matched;
    }

    private readonly IRuntimeSession session;
    private readonly StdEnumVariant enumerators;
    private readonly ConditionalWeakTable<IObjectInstance, Groups> _groups = [];

    /// <param name="session">The session the objects are objects of.</param>
    /// <param name="enumerators">Makes the object a <c>For Each</c> over the groups is driven by.</param>
    public StdSubMatches(IRuntimeSession session, StdEnumVariant enumerators)
    {
        this.session = session;
        this.enumerators = enumerators;
    }

    /// <inheritdoc/>
    public VBRuntimeObjectId? Receiver { get; set; }

    /// <summary>
    /// Makes what the groups of a match matched, or <see langword="null"/> when the library has no such class.
    /// </summary>
    public VBObjectValue? Create(string?[] matched)
    {
        if (StdLibObjects.New(session, "SubMatches") is not { } made)
        {
            return null;
        }

        _groups.Add(made.Instance, new Groups(matched));
        return made.Object;
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBLongValue> Count()
        => TryGetGroups(out var groups)
            ? RuntimeSemanticsEvaluationResult<VBLongValue>.Success(new VBLongValue(groups.Matched.Length))
            : RuntimeSemanticsEvaluationResult<VBLongValue>.InternalError();

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBVariantValue> Item(VBLongValue index)
    {
        if (!TryGetGroups(out var groups))
        {
            return RuntimeSemanticsEvaluationResult<VBVariantValue>.InternalError();
        }

        var position = index.Value;
        return position < 0 || position >= groups.Matched.Length
            ? RuntimeSemanticsEvaluationResult<VBVariantValue>.Error(StdLibObjects.OutOfRange(position, groups.Matched.Length))
            : RuntimeSemanticsEvaluationResult<VBVariantValue>.Success(ValueOf(groups.Matched[position]));
    }

    /// <inheritdoc/>
    public RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum()
        => TryGetGroups(out var groups) && enumerators.Create([.. groups.Matched.Select(matched => (VBTypedValue)ValueOf(matched))]) is { } enumerator
            ? RuntimeSemanticsEvaluationResult<VBObjectValue>.Success(enumerator)
            : RuntimeSemanticsEvaluationResult<VBObjectValue>.InternalError();

    // what a group matched, or Empty for one that took no part in the match.
    private static VBVariantValue ValueOf(string? matched)
        => new(matched is null ? VBEmptyValue.Empty : new VBStringValue(matched));

    private bool TryGetGroups(out Groups groups)
    {
        groups = null!;
        return Receiver is { } receiver
            && session.Symbols.TryGetInstance(receiver, out var instance)
            && _groups.TryGetValue(instance, out groups!);
    }
}
