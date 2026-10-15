#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types;

/// <summary>
/// A <see cref="VBIntrinsicType{int}"/> representing the <c>Error</c> data type.
/// </summary>
/// <remarks>
/// The <em>managed type</em> of a value of this data type is <c>int</c>.<br/>
/// 👉 This data type has no declaration semantics and is only indirectly usable as a <c>Variant</c> subtype.
/// <see cref="VBMissingType"/> is a kind of it: the <c>Missing</c> value is an error value.
/// </remarks>
public record class VBErrorType : VBIntrinsicType<int>
{
    /// <summary>
    /// Creates the <c>Error</c> data type. Use <see cref="TypeInfo"/>.
    /// </summary>
    public VBErrorType() : this(VBTypeNames.VBError) { }

    /// <summary>
    /// Creates a data type whose values are error values under another name.
    /// </summary>
    /// <param name="name">The name of the data type.</param>
    protected VBErrorType(string name) : base(name) { }

    private static readonly Lazy<VBErrorType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// The <c>Error</c> data type.
    /// </summary>
    public static VBErrorType TypeInfo => _instance.Value;

    private static readonly Lazy<VBErrorValue> _defaultValue = new(() => VBErrorType.Zero, LazyThreadSafetyMode.PublicationOnly);
    public override VBTypedValue DefaultValue => _defaultValue.Value;

    private static readonly Lazy<VBErrorValue> _zero = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the value <c>0</c> (zero) representation of this data type.
    /// </summary>
    public static VBErrorValue Zero => _zero.Value;

    private static readonly Lazy<VBErrorValue> _minValue = new(() => new(ushort.MinValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the minimum representable value for this data type.
    /// </summary>
    public static VBErrorValue MinValue => _minValue.Value;

    private static readonly Lazy<VBErrorValue> _maxValue = new(() => new(ushort.MaxValue), LazyThreadSafetyMode.PublicationOnly);
    /// <summary>
    /// Gets the maximum representable value for this data type.
    /// </summary>
    public static VBErrorValue MaxValue => _maxValue.Value;

    /// <summary>
    /// Gets the minimum value for a <em>standard error code</em>.
    /// </summary>
    public static int MinimumStdErrorValue => ushort.MinValue;
    /// <summary>
    /// Gets the maximum value for a <em>standard error code</em>.
    /// </summary>
    public static int MaximumStdErrorValue => ushort.MaxValue;


    public override VBTypedValue CreateValue(RDCore.SDK.Model.Values.Bindings.IBindingHandle handle) => new VBErrorValue(handle);
}
