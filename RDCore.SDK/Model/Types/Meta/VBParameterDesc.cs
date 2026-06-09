using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types.Meta;

/// <summary>
/// A meta-type that describes a <see cref="VBParameterSymbol"/>.
/// </summary>
/// <param name="Name">The <em>identifier name</em> of the parameter.</param>
/// <param name="IsByRef"><c>true</c> if the parameter is (or must be) passed by reference.</param>
public record class VBParameterDesc(string Name, bool IsByRef) : VBType(typeof(Type), Name, isHidden: true)
{
    public override int Size => sizeof(int);

    // NOTE: a value of this type is VBUnknown until determined with name resolution semantics.
    private static readonly Lazy<VBTypedValue> _defaultValue = new(() => VBUnknownValue.DefaultValue, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;
}

/// <summary>
/// Describes a <em>deferred parameter</em>; an accepted but unresolved parameter of an existing or deferred module member.
/// </summary>
/// <remarks>
/// Encountering a <em>deferred parameter</em> dring semantic traversal attaches the required semantics to produce a <c>VBInferredParameter</c> that can be materialized into a code action.<br/>
/// </remarks>
/// <param name="Name">The <em>identifier name</em> of the deferred member</param>
public record class VBDeferredParameterDesc(string Name, bool IsByRef) : VBParameterDesc(Name, IsByRef)
{
    private static readonly Lazy<VBDeferredParameterDesc> _instance = new(() => new(nameof(VBType), IsByRef: false), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Describes a specific <em>deferred</em> parameter of a module member (may also be a deferred type).
    /// </summary>
    /// <remarks>
    /// 👉 The member owning this parameter may <em>also</em> be deferred.
    /// </remarks>
    public static VBDeferredParameterDesc TypeInfo => _instance.Value;
}
