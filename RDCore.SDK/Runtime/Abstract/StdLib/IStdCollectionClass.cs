using RDCore.SDK.Model;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.3.1 Collection Object</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the <c>Collection</c> class: a sequence of values, each reachable by its position and, when it was added with a key, by that.
/// <para>
/// 👉 A collection has state of its own - its members - so unlike the error object it is one of many: each <c>New Collection</c> is an instance, and an implementation
/// keeps what is in it by the object it is called on.
/// </para>
/// <para>
/// <see cref="Item"/> is the default member of the class (<see cref="WellKnownDispIds.Value"/>), so <c>c(1)</c> is <c>c.Item(1)</c>, and <see cref="NewEnum"/> is its enumeration
/// member (<see cref="WellKnownDispIds.NewEnum"/>), which is what makes a <c>For Each</c> over a collection find its members
/// (<strong>RD-VBAL §5.4.2.4</strong>).
/// </para>
/// </remarks>
[StdLibClass]
public interface IStdCollectionClass
{
    #region 6.1.3.1.1 Public Functions
    /// <summary>
    /// Returns the number of members in the collection.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult<VBLongValue> Count();

    /// <summary>
    /// Retrieves a specific member either by <em>position</em> (index) or by <em>key</em>.
    /// </summary>
    /// <remarks>
    /// 👉 Object collections want to be <em>enumerated</em>, not indexed. Indexed access within a loop should raise some performance-related semantic flags.
    /// </remarks>
    /// <param name="index">A number from 1 to <see cref="Count"/>, or the key the member was added with.<br/>
    /// 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> if no member is at that position or has that key.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    [StdLibMember(UserMemId = WellKnownDispIds.Value)]
    RuntimeSemanticsEvaluationResult<VBVariantValue> Item(VBVariantValue index);
    #endregion

    #region 6.1.3.1.2 Public Subroutines
    /// <summary>
    /// Adds a member to the collection; the member is <em>appended</em> unless specified otherwise.
    /// </summary>
    /// <remarks>
    /// 👉 Object collections want to be <em>enumerated</em>, not indexed. Indexed access within a loop should raise some performance-related semantic flags.
    /// </remarks>
    /// <param name="item">An expression of any type that specifies the member to be added to the collection.</param>
    /// <param name="key">A <see cref="VBStringValue"/> that is unique across the keys of the collection, and can be used in place of a <em>positional index</em> to access the member.<br/>
    /// 💥<see cref="VBRuntimeErrorId.KeyAlreadyAssociatedWithAnElementOfCollection"/> if the specified key is already in use.</param>
    /// <param name="before">A position or a key: the member to be added is placed <em>before</em> the member it identifies.<br/>
    /// 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> if <strong>both</strong> <em>before</em> and <em>after</em> are specified, or if either refers to a member that does not exist.</param>
    /// <param name="after">A position or a key: the member to be added is placed <em>after</em> the member it identifies.<br/>
    /// 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> if <strong>both</strong> <em>before</em> and <em>after</em> are specified, or if either refers to a member that does not exist.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Add(VBVariantValue item, VBVariantValue? key = default, VBVariantValue? before = default, VBVariantValue? after = default);

    /// <summary>
    /// Removes a member from the collection.
    /// </summary>
    /// <param name="index">The <em>key</em> or <em>positional index</em> of the member to be removed.<br/>
    /// 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> if no member is at that position or has that key.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult Remove(VBVariantValue index);
    #endregion

    #region Enumeration
    /// <summary>
    /// Returns the object that enumerates the collection: what a <c>For Each</c> over it drives.
    /// </summary>
    /// <remarks>
    /// The enumerator is a snapshot of the members the collection has when it is asked for: a member added or removed while a <c>For Each</c> runs is not the
    /// loop's. It is a hidden member, <c>_NewEnum</c>, which VBA source reaches as <c>[_NewEnum]</c>.
    /// </remarks>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> whose value is an object of the <c>IEnumVARIANT</c> class (<see cref="IStdEnumVariantClass"/>).</returns>
    [StdLibMember("_NewEnum", ReturnType = typeof(IStdEnumVariantClass), IsHidden = true, UserMemId = WellKnownDispIds.NewEnum)]
    RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum();
    #endregion
}
