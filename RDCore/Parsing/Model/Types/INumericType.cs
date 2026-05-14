namespace RDCore.Parsing.Model.Types;

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
internal interface INumericType { }

//[AttributeUsage(AttributeTargets.Class)]
//internal class NumericTypeAttribute : Attribute { }

/// <summary>
/// Marks a <c>VBType</c> as an integral numeric data type.
/// </summary>
internal interface IIntegralNumericType : INumericType { }

//[AttributeUsage(AttributeTargets.Class)]
//internal class IntegralNumericTypeAttribute : NumericTypeAttribute { }

/// <summary>
/// Marks a <c>VBType</c> as a floating-point numeric data type.
/// </summary>
internal interface IFloatingPointNumericType : INumericType { }

//[AttributeUsage(AttributeTargets.Class)]
//internal class FloatingPointNumericTypeAttribute : NumericTypeAttribute { }

/// <summary>
/// Marks a <c>VBType</c> as a fixed-point numeric data type.
/// </summary>
internal interface IFixedPointNumericType : INumericType { }

//[AttributeUsage(AttributeTargets.Class)]
//internal class FixedPointNumericTypeAttribute : NumericTypeAttribute { }
