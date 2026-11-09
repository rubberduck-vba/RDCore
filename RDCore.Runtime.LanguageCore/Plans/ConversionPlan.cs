using RDCore.SDK.Model.Types.Abstract;

namespace RDCore.Runtime.LanguageCore.Plans;

/// <summary>
/// What a let-coercion does, decided from the type of the value and the type it is let-coerced to - before, and apart from, the value itself (IR lowering
/// §3.2).
/// </summary>
/// <remarks>
/// <para>
/// A plan is the value-independent half of a let-coercion: the rule of <strong>MS-VBAL §5.5.1.2</strong> that applies, or that none does. The other half,
/// the conversion of a value by that rule - which may overflow, fail to read a number from a string, or ask an object for its default member - is the
/// runtime's.
/// </para>
/// <para>
/// Like an <see cref="OperationPlan"/>, a plan made for the <em>value type</em> of the source - the type of what a Variant holds - is final, and a plan made
/// for a source <em>declared</em> <c>Variant</c> is <see cref="IsDeferred"/>: the value it will hold decides.
/// </para>
/// </remarks>
/// <param name="Source">The type the plan was made for: the value type of the source, or the type a deferred plan's source is declared as.</param>
/// <param name="Destination">
/// The type the value is let-coerced to: the destination's declared type, but for an Enum, whose values are <c>Long</c> (<strong>§5.2.3.4</strong>), and
/// <c>LongPtr</c>, an alias of <c>LongLong</c> in a 64-bit environment and of <c>Long</c> in a 32-bit one (<strong>§2.2</strong>).
/// </param>
/// <param name="Rule">The rule that applies, <see cref="ConversionRule.None"/> when none does or the plan is deferred.</param>
/// <param name="IsDeferred">Whether the plan depends on the value a source declared <c>Variant</c> holds, and is made at run time.</param>
public sealed record class ConversionPlan(VBType Source, VBType Destination, ConversionRule Rule, bool IsDeferred = false)
{
    /// <summary>
    /// Whether no rule applies: run-time error 13, <em>Type mismatch</em>.
    /// </summary>
    public bool IsTypeMismatch => Rule == ConversionRule.None && !IsDeferred;

    /// <summary>
    /// Whether the rule is the source's - an object, <c>Null</c>, <c>Empty</c> or an <c>Error</c> - rather than the destination's.
    /// </summary>
    public bool IsBySource => Rule is ConversionRule.FromObject or ConversionRule.FromNull or ConversionRule.FromEmpty or ConversionRule.FromError;
}
