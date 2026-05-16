namespace RDCore.Parsing.Model.Types.Abstract;

//[AttributeUsage(AttributeTargets.Class)]
//internal class IntegralNumericTypeAttribute : NumericTypeAttribute { }

/// <summary>
/// Marks a <c>VBType</c> as a floating-point numeric data type.
/// </summary>
internal interface IFloatingPointNumericType : INumericType { }

//[AttributeUsage(AttributeTargets.Class)]
//internal class FixedPointNumericTypeAttribute : NumericTypeAttribute { }
