namespace RDCore.Runtime.LanguageCore.Static.Abstract;

/// <summary>
/// The binary logical operators' static semantics (<strong>MS-VBAL 5.6.9.8</strong>): the declared type of each is the plan of its operation, made for the
/// declared types of its operands (<see cref="PlannedOperatorStaticSemantics"/>).
/// </summary>
/// <param name="Token">The operator: <c>And</c>, <c>Or</c>, <c>Xor</c>, <c>Eqv</c> or <c>Imp</c>.</param>
public record class BinaryLogicalOperatorStaticSemantics(string Token) : PlannedOperatorStaticSemantics
{
    protected override string Operator => Token;
}
