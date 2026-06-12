#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A <see cref="VBNumericType{Int32}"/> representing the <c>Long</c> data type.
    /// </summary>
    /// <remarks>
    /// The <em>managed type</em> of a value of this data type is <c>int</c>.<br/>
    /// 👉 Implements <see cref="IIntegralNumericType"/>.
    /// </remarks>
    public record class VBLongType() : VBNumericType<int>(VBTypeNames.VBLong), IIntegralNumericType
    {
        private static readonly Lazy<VBLongType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>Long</c> data type.
        /// </summary>
        public static VBLongType TypeInfo => _instance.Value;

        private static readonly Lazy<VBLongValue> _minValue = new(() 
            => new VBLongValue(GlobalSymbols.ExtensionSymbols.VBLongMinValue) { ManagedValue = int.MinValue }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the minimum representable value for this data type.
        /// </summary>
        public static VBLongValue MinValue => _minValue.Value;
        public override double ManagedMinValue => _minValue.Value.ManagedValue;

        private static readonly Lazy<VBLongValue> _maxValue = new(()
            => new VBLongValue(GlobalSymbols.ExtensionSymbols.VBLongMaxValue) { ManagedValue = int.MaxValue }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the maximum representable value for this data type.
        /// </summary>
        public static VBLongValue MaxValue => _maxValue.Value;
        public override double ManagedMaxValue => _maxValue.Value.ManagedValue;

        private static readonly Lazy<VBLongValue> _zero = new(()
            => new VBLongValue(GlobalSymbols.ExtensionSymbols.VBLongZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the value <c>0</c> (zero) representation of this data type.
        /// </summary>
        public static VBLongValue Zero => _zero.Value;

        public override VBTypedValue DefaultValue => VBLongType.Zero;
        public override int Size => sizeof(int);
    }
}
