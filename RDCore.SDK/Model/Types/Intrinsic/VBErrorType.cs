#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A <see cref="VBIntrinsicType{int}"/> representing the <c>Error</c> data type.
    /// </summary>
    /// <remarks>
    /// The <em>managed type</em> of a value of this data type is <c>int</c>.<br/>
    /// 👉 This data type has no declaration semantics and is only indirectly usable as a <c>Variant</c> subtype.
    /// </remarks>
    public sealed record class VBErrorType() : VBIntrinsicType<int>(VBTypeNames.VBError)
    {
        private static readonly Lazy<VBErrorType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>Error</c> data type.
        /// </summary>
        public static VBErrorType TypeInfo => _instance.Value;

        private static readonly Lazy<VBErrorValue> _defaultValue = new(() => VBErrorType.Zero, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;

        private static readonly Lazy<VBErrorValue> _zero = new(() => new(GlobalSymbols.ExtensionSymbols.VBErrorZeroValue), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the value <c>0</c> (zero) representation of this data type.
        /// </summary>
        public static VBErrorValue Zero => _zero.Value;

        private static readonly Lazy<VBErrorValue> _minValue = new(() => new(GlobalSymbols.ExtensionSymbols.VBErrorMinValue, ushort.MinValue), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the minimum representable value for this data type.
        /// </summary>
        public static VBErrorValue MinValue => _minValue.Value;

        private static readonly Lazy<VBErrorValue> _maxValue = new(() => new(GlobalSymbols.ExtensionSymbols.VBErrorMaxValue, ushort.MaxValue), LazyThreadSafetyMode.PublicationOnly);
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

        public override int Size => sizeof(int);
    }
}
