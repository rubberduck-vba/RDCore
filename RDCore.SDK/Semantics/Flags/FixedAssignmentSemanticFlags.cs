namespace RDCore.SDK.Semantics.Flags;

/// <summary>
/// The semantic flags that can be attached to an <c>LSet</c> or <c>RSet</c> statement (<strong>MS-VBAL §5.4.3.6-7</strong>) operation.
/// </summary>
/// <remarks>
/// 🧩 The statement itself says nothing about a program that is wrong in a way it can still run: the facts are flagged here, at analysis time, and it is
/// for an analyzer (<c>RDCore.Diagnostics</c>, or any other) to decide whether one is worth a diagnostic.
/// </remarks>
[Flags]
public enum FixedAssignmentSemanticFlags
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
    /// The target is a <c>String</c>: the value is fitted into the width the target already has.
    /// </summary>
    StringTarget = 1 << 1,
    /// <summary>
    /// The target and the source are user-defined types: <c>LSet</c> copies one over the other as bytes.
    /// </summary>
    UserDefinedTypeCopy = 1 << 2,
    /// <summary>
    /// The <em>source</em> of a <see cref="UserDefinedTypeCopy"/> is a user-defined type that has a variable-length <c>String</c> member, at any depth.
    /// </summary>
    /// <remarks>
    /// 👉 MS-VBA copies the member's <em>pointer</em>, and leaves two records owning one allocation: which is why the statement is a known way to corrupt a
    /// VBA process. RD-VBA copies the value instead and corrupts nothing, but a program written against MS-VBA's behaviour is relying on something that it
    /// should be told about either way.
    /// </remarks>
    SourceHoldsVariableLengthString = 1 << 3,
    /// <summary>
    /// The <em>destination</em> of a <see cref="UserDefinedTypeCopy"/> is a user-defined type that has a variable-length <c>String</c> member, at any depth.
    /// </summary>
    /// <remarks>
    /// 👉 See <see cref="SourceHoldsVariableLengthString"/>.
    /// </remarks>
    DestinationHoldsVariableLengthString = 1 << 4,
}
