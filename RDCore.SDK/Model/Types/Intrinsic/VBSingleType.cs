#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A <see cref="VBNumericType{Single}"/> representing the <c>Single</c> data type.
    /// </summary>
    /// <remarks>
    /// The <em>managed type</em> of a value of this data type is <c>float</c>.<br/>
    /// 👉 Implements <see cref="IFloatingPointNumericType"/>.
    /// </remarks>
    public record class VBSingleType() : VBNumericType<float>(VBTypeNames.VBSingle), IFloatingPointNumericType
    {
        /// <summary>
        /// The number of significant digits retained in a String representation of a value of this type.
        /// </summary>
        public const int SignificantIntegerDigits = 7;

        private static readonly Lazy<VBSingleType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>Single</c> data type.
        /// </summary>
        public static VBSingleType TypeInfo => _instance.Value;

        private static readonly Lazy<VBSingleValue> _defaultValue = new(() => VBSingleType.Zero, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;

        private static readonly Lazy<VBSingleValue> _minValue = new(() => new VBSingleValue(GlobalSymbols.ExtensionSymbols.VBSingleMinValue) { ManagedValue = float.MinValue }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the minimum representable value for this data type.
        /// </summary>
        public static VBSingleValue MinValue => _minValue.Value;
        public override double ManagedMinValue => _minValue.Value.ManagedValue;

        private static readonly Lazy<VBSingleValue> _maxValue = new(() => new VBSingleValue(GlobalSymbols.ExtensionSymbols.VBSingleMaxValue) { ManagedValue = float.MaxValue }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the maximum representable value for this data type.
        /// </summary>
        public static VBSingleValue MaxValue => _maxValue.Value;
        public override double ManagedMaxValue => _maxValue.Value.ManagedValue;

        private static readonly Lazy<VBSingleValue> _zero = new(() => new VBSingleValue(GlobalSymbols.ExtensionSymbols.VBSingleZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the value <c>0</c> (zero) representation of this data type.
        /// </summary>
        public static VBSingleValue Zero => _zero.Value;

        public override int Size => sizeof(float);
    }
}