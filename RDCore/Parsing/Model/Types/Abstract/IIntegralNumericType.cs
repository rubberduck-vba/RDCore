namespace RDCore.Parsing.Model.Types.Abstract;

//[AttributeUsage(AttributeTargets.Class)]
//internal class NumericTypeAttribute : Attribute { }

/// <summary>
/// Marks a <c>VBType</c> as an integral numeric data type.
/// </summary>
internal interface IIntegralNumericType : INumericType { }

//[AttributeUsage(AttributeTargets.Class)]
//internal class FixedPointNumericTypeAttribute : NumericTypeAttribute { }
