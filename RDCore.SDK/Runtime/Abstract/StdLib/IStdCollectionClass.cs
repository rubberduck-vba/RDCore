using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.3.1 Collection Class</strong>
/// </summary>
/// <remarks>
/// Formalizes the public interface of the <c>Collection</c> class.
/// </remarks>
public interface IStdCollectionClass
{
    #region 6.1.3.1.1 Public Functions
    /// <summary>
    /// Returns the number of items in the collection.
    /// </summary>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdCollectionClass__Count();
    /// <summary>
    /// Retrieves a specific item either by <em>position</em> (index) or by <em>key</em>.
    /// </summary>
    /// <remarks>
    /// 👉 Object collections want to be <em>enumerated</em>, not indexed. Indexed access within a loop should raise some performance-related semantic flags.
    /// </remarks>
    /// <param name="index"></param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdCollectionClass__Item(VBVariantValue index);
    #endregion

    #region 6.1.3.2 Public Procedures
    /// <summary>
    /// Adds an item to the collection; the item is <em>appended</em> unless specified otherwise.
    /// </summary>
    /// <remarks>
    /// 👉 Object collections want to be <em>enumerated</em>, not indexed. Indexed access within a loop should raise some performance-related semantic flags.
    /// </remarks>
    /// <param name="item">An expression of any type that specifies the item to be added to the collection.</param>
    /// <param name="key">A <see cref="VBStringValue"/> that is unique across all keys in the collections. Can be used in place of a <em>positional index</em> to access an item in the collection.<br/>
    /// 💥<see cref="VBRuntimeErrorId.KeyAlreadyAssociatedWithAnElementOfCollection"/> if the specified key is already in use.</param>
    /// <param name="before">An expression that specifies a relative position in the collection; the item to be added is placed <em>before</em> the item identified by this parameter.<br/>
    /// 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> if <strong>both</strong> <em>before</em> and <em>after</em> optional parameters are specified, or if they refer to non-existing items.</param>
    /// <param name="after">An expression that specifies a relative position in the collection; the item to be added is placed <em>after</em> the item identified by this parameter.<br/>
    /// 💥<see cref="VBRuntimeErrorId.InvalidProcedureCallOrArgument"/> if <strong>both</strong> <em>before</em> and <em>after</em> optional parameters are specified, or if they refer to non-existing items.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdCollectionClass__Add(VBVariantValue item, VBVariantValue? key = default, VBVariantValue? before = default, VBVariantValue? after = default);

    /// <summary>
    /// Removes an item from the collection.
    /// </summary>
    /// <param name="index">The <em>key</em> or <em>positional index</em> of the item to be removed.<br/>
    /// 💥<see cref="VBRuntimeErrorId.MethodOrDataMemberNotFound"/> if no item exists at the specified positional index or with the specified key.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    RuntimeSemanticsEvaluationResult StdCollectionClass__Remove(VBVariantValue index);
    #endregion
}
