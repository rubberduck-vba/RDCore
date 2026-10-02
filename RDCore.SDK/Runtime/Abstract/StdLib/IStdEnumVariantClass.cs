using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// The enumerator a <c>For Each</c> drives over an object that is not an array (<strong>MS-VBAL §5.4.2.4</strong>).
/// </summary>
/// <remarks>
/// MS-VBAL leaves the enumeration of an object "in an implementation-defined manner": the object's enumeration member (<c>_NewEnum</c>,
/// <c>VB_UserMemId</c> <c>-4</c>) returns an enumerator, and the loop asks it for each member in turn. In MS-VBA the enumerator is a COM
/// <c>IEnumVARIANT</c>, whose <c>Next</c> fills an array of <c>Variant</c>s that VBA source cannot call; this is the same enumerator in a shape that source can,
/// and is how a class written in VBA says what its members are: a class whose <c>_NewEnum</c> returns an object of a class that implements this one, or
/// the enumerator of another collection, <c>mItems.[_NewEnum]</c>.
/// <para>
/// 👉 Every member is hidden: it is no part of what VBA source is meant to call, and it is not creatable.
/// </para>
/// </remarks>
[StdLibClass("IEnumVARIANT", IsCreatable = false)]
public interface IStdEnumVariantClass
{
    /// <summary>
    /// Moves to the next member.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> whose value is <c>True</c> when there is one, and <c>False</c> when the members have run out.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult<VBBooleanValue> MoveNext();

    /// <summary>
    /// The member the enumerator is at: the one <see cref="MoveNext"/> last moved to.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> whose value is the member.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult<VBVariantValue> Current();

    /// <summary>
    /// Returns to before the first member.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(IsHidden = true)]
    RuntimeSemanticsEvaluationResult Reset();
}
