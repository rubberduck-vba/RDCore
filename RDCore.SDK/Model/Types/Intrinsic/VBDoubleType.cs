#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A <see cref="VBNumericType{Double}"/> representing the <c>Double</c> data type.
    /// </summary>
    /// <remarks>
    /// The <em>managed type</em> of a value of this data type is <c>double</c>.<br/>
    /// 👉 Implements <see cref="IFloatingPointNumericType"/>.
    /// </remarks>
    public sealed record class VBDoubleType() : VBNumericType<double>(VBTypeNames.VBDouble), IFloatingPointNumericType
    {
        /// <summary>
        /// The number of significant digits retained in a String representation of a value of this type.
        /// </summary>
        public const int SignificantIntegerDigits = 15;

        private static readonly Lazy<VBDoubleValue> _minValue = new(() => new(GlobalSymbols.ExtensionSymbols.VBDoubleMinValue) { ManagedValue = double.MinValue * Math.Pow(10, -4), TypeInfo = VBDoubleType.TypeInfo }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the minimum representable value for this data type.
        /// </summary>
        public static VBDoubleValue MinValue => _minValue.Value;
        public override double ManagedMinValue => _minValue.Value.ManagedValue;

        private static readonly Lazy<VBDoubleValue> _maxValue = new(() => new(GlobalSymbols.ExtensionSymbols.VBDoubleMaxValue) { ManagedValue = double.MaxValue * Math.Pow(10, -4), TypeInfo = VBDoubleType.TypeInfo }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the maximum representable value for this data type.
        /// </summary>
        public static VBDoubleValue MaxValue => _maxValue.Value;
        public override double ManagedMaxValue => _maxValue.Value.ManagedValue;

        private static readonly Lazy<VBDoubleValue> _zero = new(() => new(GlobalSymbols.ExtensionSymbols.VBDoubleZeroValue) { ManagedValue = 0, TypeInfo = VBDoubleType.TypeInfo }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the value <c>0</c> (zero) representation of this data type.
        /// </summary>
        public static VBDoubleValue Zero => _zero.Value;
        private static readonly Lazy<VBDoubleValue> _one = new(() => new (GlobalSymbols.ExtensionSymbols.VBDoubleOneValue), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the value <c>1</c> (one) representation of this data type.
        /// </summary>
        /// <remarks>
        /// Used for returning a constant 1 from certain runtime semantics.
        /// </remarks>
        public static VBDoubleValue One => _one.Value;

        private static readonly Lazy<VBDoubleValue> _defaultValue = new(() => VBDoubleType.Zero, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;

        private static readonly Lazy<VBDoubleType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>Double</c> data type.
        /// </summary>
        public static VBDoubleType TypeInfo => _instance.Value;

        public override int Size => sizeof(double);
    }
}
