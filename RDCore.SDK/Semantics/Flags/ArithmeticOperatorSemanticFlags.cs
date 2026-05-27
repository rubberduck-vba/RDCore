namespace RDCore.SDK.Semantics.Runtime.Operators;

[Flags]
public enum ArithmeticOperatorSemanticFlags
{
    /// <summary>
    /// The operation is evaluated with <em>null effective type</em> semantics.
    /// </summary>
    VBNullEffectiveType = 1 << 0,
    /// <summary>
    /// The operation is evaluated with <em>numeric effective type</em> semantics.
    /// </summary>
    VBNumericEffectiveType = 1 << 1,
    /// <summary>
    /// The operation is evaluated with <em>date effective type</em> semantics.
    /// </summary>
    VBDateEffectiveType = 1 << 2,
    /// <summary>
    /// The operation is evaluated with <em>string effective type</em> semantics.
    /// </summary>
    VBStringEffectiveType = 1 << 3,
    /// <summary>
    /// The operation involves the <em>Banker's Rounding</em> algorithm.
    /// </summary>
    BankersRounding = 1 << 4,
}
