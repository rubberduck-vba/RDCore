namespace RDCore.Runtime.LanguageCore.Static.Abstract;

/// <summary>
/// The unary arithmetic operators' static semantics (<strong>MS-VBAL 5.6.9.3</strong>): the declared type of each is the plan of its operation, made for the
/// declared type of its operand (<see cref="PlannedOperatorStaticSemantics"/>).
/// </summary>
public abstract record class UnaryArithmeticOperatorStaticSemantics : PlannedOperatorStaticSemantics;
