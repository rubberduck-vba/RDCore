namespace RDCore.Parsing.Model.Types.Abstract;

//[AttributeUsage(AttributeTargets.Class)]
//internal class FloatingPointNumericTypeAttribute : NumericTypeAttribute { }

/// <summary>
/// Marks a <c>VBType</c> as a fixed-point numeric data type.
/// </summary>
internal interface IFixedPointNumericType : INumericType { }

//[AttributeUsage(AttributeTargets.Class)]
//internal class FixedPointNumericTypeAttribute : NumericTypeAttribute { }
