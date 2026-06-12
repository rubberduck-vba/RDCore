#pragma warning disable IDE0130 // Namespace does not match folder structure
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;

namespace RDCore.SDK.Model.Types
{
    /// <summary>
    /// A <see cref="VBNumericType{Int64}"/> representing the <c>LongLong</c> data type.
    /// </summary>
    /// <remarks>
    /// The <em>managed type</em> of a value of this data type is <c>long</c>.<br/>
    /// 💥 Declarations of this data type are <strong>statically invalid</strong> in a <strong>32-bit</strong> environment.<br/>
    /// 👉 Implements <see cref="IIntegralNumericType"/>.<br/>
    /// </remarks>
    public record class VBLongLongType() : VBNumericType<long>(VBTypeNames.VBLong), IIntegralNumericType
    {
        private static readonly Lazy<VBLongLongType> _instance = new(() => new(), LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// The <c>LongLong</c> data type.
        /// </summary>
        /// <remarks>
        /// This type is statically invalid in a 32-bit environment.
        /// </remarks>
        public static VBLongLongType TypeInfo => _instance.Value;

        private static readonly Lazy<VBLongLongValue> _defaultValue = new(() => VBLongLongType.Zero, LazyThreadSafetyMode.PublicationOnly);
        public override VBTypedValue DefaultValue => _defaultValue.Value;

        private static readonly Lazy<VBLongLongValue> _minValue = new(() => new VBLongLongValue(GlobalSymbols.ExtensionSymbols.VBLongLongMinValue) { ManagedValue = long.MinValue }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the minimum representable value for this data type.
        /// </summary>
        public static VBLongLongValue MinValue => _minValue.Value;
        public override double ManagedMinValue => _minValue.Value.ManagedValue;

        private static readonly Lazy<VBLongLongValue> _maxValue = new(() => new VBLongLongValue(GlobalSymbols.ExtensionSymbols.VBLongLongMaxValue) { ManagedValue = long.MaxValue }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the maximum representable value for this data type.
        /// </summary>
        public static VBLongLongValue MaxValue => _maxValue.Value;
        public override double ManagedMaxValue => _maxValue.Value.ManagedValue;

        private static readonly Lazy<VBLongLongValue> _zeroValue = new(() => new VBLongLongValue(GlobalSymbols.ExtensionSymbols.VBLongLongZeroValue) { ManagedValue = 0 }, LazyThreadSafetyMode.PublicationOnly);
        /// <summary>
        /// Gets the value <c>0</c> (zero) representation of this data type.
        /// </summary>
        public static VBLongLongValue Zero => _zeroValue.Value;

        public override int Size => sizeof(long);
    }
}
