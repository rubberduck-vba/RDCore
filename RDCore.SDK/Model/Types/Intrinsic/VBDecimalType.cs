#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A <see cref="VBNumericType{Decimal}"/> representing the <c>Decimal</c> data type.
    /// </summary>
    /// <remarks>
    /// The <em>managed type</em> of a value of this data type is <c>decimal</c>.<br/>
    /// 👉 Implements <see cref="IFixedPointNumericType"/>.
    /// </remarks>
    public sealed record class VBDecimalType() : VBNumericType<decimal>(VBTypeNames.VBDecimal), IFixedPointNumericType
    {
        private static readonly Lazy<VBDecimalType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>Decimal</c> data type.
        /// </summary>
        public static VBDecimalType TypeInfo => _instance.Value;

        private static readonly Lazy<VBDecimalValue> _defaultValue = new(() => VBDecimalType.Zero, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;

        private static readonly Lazy<VBDecimalValue> _minValue = new(() => new VBDecimalValue(GlobalSymbols.ExtensionSymbols.VBDecimalMinValue) { ManagedValue = (double)(long.MinValue * Math.Pow(10, -4)) }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the minimum representable value for this data type.
        /// </summary>
        public static VBDecimalValue MinValue => _minValue.Value;
        public override double ManagedMinValue => _minValue.Value.ManagedValue;

        private static readonly Lazy<VBDecimalValue> _maxValue = new(() => new VBDecimalValue(GlobalSymbols.ExtensionSymbols.VBDecimalMaxValue) { ManagedValue = (double)(long.MaxValue * Math.Pow(10, -4)) }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the maximum representable value for this data type.
        /// </summary>
        public static VBDecimalValue MaxValue => _maxValue.Value;
        public override double ManagedMaxValue => _maxValue.Value.ManagedValue;

        private static readonly Lazy<VBDecimalValue> _zero = new(() => new VBDecimalValue(GlobalSymbols.ExtensionSymbols.VBDecimalZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the value <c>0</c> (zero) representation of this data type.
        /// </summary>
        public static VBDecimalValue Zero => _zero.Value;

        public override int Size => 14; // NOTE: managed decimal is 16.
    }
}
