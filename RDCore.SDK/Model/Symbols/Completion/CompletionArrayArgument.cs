using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.SDK.Model.Symbols.Completion;

/// <summary>
/// An argument that is given an array: what a name written there must be declared as for a completion list to offer it.
/// </summary>
/// <remarks>
/// Two arguments are given an array:
/// <list type="bullet">
/// <item>the first argument of <c>LBound</c> and <c>UBound</c>, special forms (<strong>MS-VBAL §3.3.5.2</strong>) whose operand is an array, or a
/// <c>Variant</c> that may hold one when it runs: one declared as a scalar, an <c>Object</c>, a class or a user-defined type is "Expected array", and so is a
/// project, a module or an <c>Enum</c>;</item>
/// <item>an argument of a parameter declared as an array, which is passed by reference: the declared type of its argument is the parameter's exactly
/// (<strong>MS-VBAL §5.3.1.11</strong>), an array of the same element type, fixed-size or resizable. A <c>Variant</c> is not.</item>
/// </list>
/// A variable, a field (of a user-defined type too), a parameter, a <c>Function</c> and a <c>Property</c> are offered by their declared type: a
/// <c>Function</c> or a <c>Property Get</c> by the type it returns (<c>UBound(Titles())</c>). A constant, an <c>Enum</c> member and a <c>Sub</c> never are, nor
/// is a keyword or a name with no declared type.
/// </remarks>
/// <param name="ItemType">
/// The element type of the array the argument is given, for a parameter declared as an array; <see langword="null"/> when any array, or a <c>Variant</c>,
/// is given (<c>LBound</c>, <c>UBound</c>).
/// </param>
public sealed record class CompletionArrayArgument(VBType? ItemType = null)
{
    /// <summary>
    /// The argument at the cursor, when it is given an array and nothing of it is written but <paramref name="written"/>; <see langword="null"/> for any other
    /// argument, or when more is written of it.
    /// </summary>
    /// <param name="site">The call being written.</param>
    /// <param name="parameter">The parameter the argument is given to, when what is called is a procedure; <see langword="null"/> otherwise.</param>
    /// <param name="written">What is written of the argument up to the cursor: the word typed, or a qualifier, its <c>.</c> and the word typed after it.</param>
    public static CompletionArrayArgument? Of(SignatureSite site, SignatureParameter? parameter, string written)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(written);
        if (site.ArgumentName is not null || !site.IsWhereAnArgumentStarts(written))
        {
            return null;
        }

        if (parameter?.Type is VBArrayType array)
        {
            return new CompletionArrayArgument(array.ItemType);
        }

        var isArrayBound = !site.IsMemberAccess && site.IsParenthesized && site.ArgumentIndex == 0
            && (string.Equals(site.Name, Tokens.LBound, StringComparison.OrdinalIgnoreCase) || string.Equals(site.Name, Tokens.UBound, StringComparison.OrdinalIgnoreCase));
        return isArrayBound ? new CompletionArrayArgument() : null;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> can be given to the argument: a name whose declared type is an array of <see cref="ItemType"/>, or any array or a
    /// <c>Variant</c> when <see cref="ItemType"/> is <see langword="null"/>.
    /// </summary>
    /// <param name="candidate">A name a completion list would offer.</param>
    public bool Accepts(CompletionCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (candidate.Symbol is not ITypedSymbol typed
            || candidate.Symbol.Kind is not (SymbolKindExt.Variable or SymbolKindExt.Field or SymbolKindExt.Function or SymbolKindExt.Property))
        {
            return false;
        }

        return ItemType is null
            ? typed.ResolvedType is VBArrayType or VBVariantType
            : typed.ResolvedType is VBArrayType array && string.Equals(array.ItemType.Name, ItemType.Name, StringComparison.OrdinalIgnoreCase);
    }
}
