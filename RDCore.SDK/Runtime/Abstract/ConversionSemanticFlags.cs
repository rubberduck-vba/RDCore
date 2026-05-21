namespace RDCore.SDK.Runtime.Abstract;

/// <summary>
/// The semantic flags that can be attached to a <c>DataTypeConversion</c> semantic operation.
/// </summary>
[Flags]
public enum ConversionSemanticFlags
{
    /// <summary>
    /// This conversion operation occurs via an explicit <c>CType</c> data type conversion function.
    /// </summary>
    Explicit = 1 << 0,
    /// <summary>
    /// This conversion operation does not occur via an explicit <c>CType</c> data type conversion function.
    /// </summary>
    Implicit = 1 << 1,
    /// <summary>
    /// This conversion operation can be made explicit by inserting the appropriate <c>CType</c> data type conversion function call.
    /// </summary>
    CTypeAvailable = 1 << 2,
    /// <summary>
    /// This conversion operation results in a wider data type.
    /// </summary>
    Widening = 1 << 3,
    /// <summary>
    /// This conversion operation results in a narrower data type.
    /// </summary>
    Narrowing = 1 << 4,
    /// <summary>
    /// This data type conversion operation evaluates to a numeric data type.
    /// </summary>
    Numeric = 1 << 5,
    /// <summary>
    /// This conversion operation involves a <c>DateSerial</c> (<c>Double</c>) conversion from a <c>Date</c> value.
    /// </summary>
    DateSerial = 1 << 6,
    /// <summary>
    /// This conversion operation implicates a <c>VBNullValue</c> operand.
    /// </summary>
    NullOperand = 1 << 7,
}
