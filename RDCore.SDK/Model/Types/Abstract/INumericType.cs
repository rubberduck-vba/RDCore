namespace RDCore.SDK.Model.Types.Abstract
{
    /**
     * Marker interfaces used for annotating <c>VBType</c> types
     * to map static and runtime semantics to MS-VBAL specifications literally.
     * 
     * > MS-VBAL 5.5.1.2.1 Let-coercion between numeric types (runtime semantics)
     * This section describes the numeric value types referenced throughout the specifications:
     *  - Integral: Byte, Integer, Long, LongLong
     *  - Floating-point: Single and Double
     *  - Fixed-point: Currency and Decimal
     * 
    */

    /// <summary>
    /// Marks a <c>VBType</c> as a numeric data type.
    /// </summary>
    /// <remarks>
    /// This interface could be (or become) an attribute.
    /// </remarks>
    public interface INumericType { }

    //[AttributeUsage(AttributeTargets.Class)]
    //public class FixedPointNumericTypeAttribute : NumericTypeAttribute { }
}
