using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// An argument that is given an array: what a name written there must be declared as for a completion list to offer it, and where it ranks.
/// </summary>
/// <remarks>
/// <para>
/// An argument is given an array in two cases:
/// </para>
/// <list type="bullet">
/// <item><description>the first argument of <c>LBound</c> and <c>UBound</c>, special forms (<strong>MS-VBAL §3.3.5.2</strong>) whose operand is an array,
/// or a <c>Variant</c> that may hold one when it runs: one declared as a scalar, an <c>Object</c>, a class or a user-defined type is "Expected array", and
/// so is a project, a module or an <c>Enum</c>;</description></item>
/// <item><description>an argument of a parameter declared as an array, which is passed by reference, named or not: the declared type of its argument must
/// match the parameter's (<strong>MS-VBAL §5.3.1.11</strong>), so a <c>Variant</c> is not accepted there. The element types match as MS-VBA and RDCore's
/// compiler match them, which is not always the specification's letter.</description></item>
/// </list>
/// <para>
/// How the element types of an array argument match:
/// </para>
/// <list type="bullet">
/// <item><description>a <c>String * 3</c> does not match a <c>String</c>: a fixed-length <c>String</c> is a declared type of its own, as the specification
/// has it;</description></item>
/// <item><description>an array of any <c>Enum</c> matches a parameter declared as an array of an <c>Enum</c>, and an array of <c>Long</c> does not, nor
/// the reverse, though by the specification's letter a declaration that uses an <c>Enum</c> has the declared type <c>Long</c> (<strong>MS-VBAL §2.2</strong>,
/// <strong>§5.2.3.4</strong>);</description></item>
/// <item><description>a fixed-size array is accepted as a resizable one is, though by the specification's letter it is of another declared
/// type.</description></item>
/// </list>
/// <para>
/// A variable, a field (of a user-defined type too), a parameter, a <c>Function</c> and a <c>Property</c> are offered by their declared type: a
/// <c>Function</c> or a <c>Property Get</c> by the type it returns (<c>UBound(Titles())</c>). A constant, an <c>Enum</c> member and a <c>Sub</c> never are, nor
/// is a keyword or a name with no declared type.
/// </para>
/// <para>
/// What is offered ranks in four tiers (<see cref="GroupsOf"/>): the arrays the procedure declares, its own array parameters included; then the other
/// arrays, of its module, of the project, and the members that return one; then the <c>Variant</c>s, for <c>LBound</c> and <c>UBound</c>; then the records
/// and the objects whose member is an array the argument can be given, or leads to one through its own members, three members at most in all
/// (<c>UBound(o.Inner.Slots)</c> is two, <c>UBound(cb.C.D.Arr)</c> three). Such a record or object is offered so that its member can be written after it;
/// a record or an object that leads to no such array is not offered.
/// </para>
/// </remarks>
/// <param name="ItemType">
/// The element type of the array the argument is given, for a parameter declared as an array; <see langword="null"/> when any array, or a <c>Variant</c>,
/// is given (<c>LBound</c>, <c>UBound</c>).
/// </param>
public sealed record class CompletionArrayArgument(VBType? ItemType = null)
{
    // how many members are written after a record or an object to reach an array: o.Inner.Slots is two, cb.C.D.Arr three.
    private const int MaxMemberDepth = 3;

    private const int LocalArrays = 0;
    private const int OtherArrays = 1;
    private const int Variants = 2;
    private const int LeadsToAnArray = 3;

    /// <summary>
    /// The argument at the cursor, when it is given an array and nothing of its value is written but <paramref name="written"/>; <see langword="null"/> for any
    /// other argument, or when more is written of it.
    /// </summary>
    /// <param name="site">The call being written.</param>
    /// <param name="parameter">The parameter the argument is given to, when what is called is a procedure; <see langword="null"/> otherwise.</param>
    /// <param name="written">What is written of the argument's value up to the cursor: the word typed, or a qualifier, its <c>.</c> and the word typed after it.</param>
    public static CompletionArrayArgument? Of(SignatureSite site, SignatureParameter? parameter, string written)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(written);
        if (!site.IsWhereAnArgumentsValueStarts(written))
        {
            return null;
        }

        if (parameter?.Type is VBArrayType array)
        {
            return new CompletionArrayArgument(array.ItemType);
        }

        var isArrayBound = site.ArgumentName is null && !site.IsMemberAccess && site.IsParenthesized && site.ArgumentIndex == 0
            && (string.Equals(site.Name, Tokens.LBound, StringComparison.OrdinalIgnoreCase) || string.Equals(site.Name, Tokens.UBound, StringComparison.OrdinalIgnoreCase));
        return isArrayBound ? new CompletionArrayArgument() : null;
    }

    /// <summary>
    /// The candidates that can be given to the argument, or lead to what can be, in the tiers they rank in, each by name: the local arrays, the other arrays,
    /// the <c>Variant</c>s, and the records and objects that lead to an array. A candidate that is none of them is left out.
    /// </summary>
    /// <param name="candidates">The names a completion list would offer.</param>
    /// <param name="resolver">
    /// The resolver names are bound by, which a class or a user-defined type of the workspace is read from as the composition has it now
    /// (<see cref="CurrentTypes"/>); <see langword="null"/> when there is none.
    /// </param>
    /// <returns>The four tiers, in the order they rank, for <see cref="CompletionCandidates.ListOf"/>.</returns>
    public IReadOnlyList<CompletionGroup> GroupsOf(IEnumerable<CompletionCandidate> candidates, ISymbolResolver? resolver)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var tiers = new[] { new List<CompletionCandidate>(), [], [], [] };
        var leads = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (TierOf(candidate, resolver, leads) is { } tier)
            {
                tiers[tier].Add(candidate);
            }
        }

        return [.. tiers.Select(tier => new CompletionGroup([.. tier.OrderBy(candidate => candidate.Label.ToLowerInvariant(), StringComparer.Ordinal)]))];
    }

    private int? TierOf(CompletionCandidate candidate, ISymbolResolver? resolver, Dictionary<string, bool> leads)
    {
        if (!IsOffered(candidate, out var type))
        {
            return null;
        }

        return type switch
        {
            VBArrayType array when IsGiven(array) => candidate.Rank == CompletionCandidates.ProcedureRank && candidate.Symbol!.Kind == SymbolKindExt.Variable
                ? LocalArrays
                : OtherArrays,
            VBVariantType when ItemType is null => Variants,
            _ when LeadsToAGivenArray(type, resolver, leads, MaxMemberDepth) => LeadsToAnArray,
            _ => null,
        };
    }

    // a name that is offered by its declared type: a variable, a field, a parameter, a function or a property.
    private static bool IsOffered(CompletionCandidate candidate, out VBType type)
    {
        type = candidate.Symbol is ITypedSymbol typed ? typed.ResolvedType : VBUnknownType.TypeInfo;
        return candidate.Symbol is ITypedSymbol
            && candidate.Symbol.Kind is SymbolKindExt.Variable or SymbolKindExt.Field or SymbolKindExt.Function or SymbolKindExt.Property;
    }

    private bool IsGiven(VBArrayType array) => ItemType is null || VBArrayType.SameElementType(ItemType, array.ItemType);

    // whether a member of a record or an object, or a member of one of its members, is an array the argument can be given. What is found of a type is
    // kept for the depth it was looked into at, by the type's declaration (a Private Type of one name in each of two modules is two types), and a type being
    // looked into leads to nothing more through itself.
    private bool LeadsToAGivenArray(VBType type, ISymbolResolver? resolver, Dictionary<string, bool> leads, int depth)
    {
        var current = CurrentTypes.Of(resolver, type);
        Symbol? declaration = current switch
        {
            VBClassType declared => declared.Symbol,
            VBUserDefinedType declared => declared.Symbol,
            _ => null,
        };

        if (declaration is null || depth == 0)
        {
            return false;
        }

        var key = $"{depth}:{declaration.Uri.AbsoluteUri}";
        if (leads.TryGetValue(key, out var known))
        {
            return known;
        }

        leads[key] = false;
        var found = CompletionCandidates.MembersOf(current).Any(member => IsOffered(member, out var memberType)
            && (memberType is VBArrayType array && IsGiven(array) || LeadsToAGivenArray(memberType, resolver, leads, depth - 1)));
        leads[key] = found;
        return found;
    }
}
