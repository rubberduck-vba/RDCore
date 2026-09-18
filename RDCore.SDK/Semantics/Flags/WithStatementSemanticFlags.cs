namespace RDCore.SDK.Semantics.Flags;

/// <summary>
/// The semantic flags that can be attached to a <c>With</c> statement (<strong>MS-VBAL 5.4.2.21</strong>) operation.
/// </summary>
[Flags]
public enum WithStatementSemanticFlags
{
    /// <summary>
    /// No flags set.
    /// </summary>
    None = 0,
    /// <summary>
    /// This operation produces a run-time error.
    /// </summary>
    Failed = 1 << 0,
    /// <summary>
    /// The <c>With</c> block's target expression is Set-assigned (its value type is a class).
    /// </summary>
    ClassTarget = 1 << 1,
    /// <summary>
    /// The <c>With</c> block's target expression is Let-assigned (its value type is a UDT).
    /// </summary>
    UserDefinedTypeTarget = 1 << 2,
}
