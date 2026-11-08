namespace RDCore.Runtime.LanguageCore.Static.Abstract;

/// <summary>
/// The binary arithmetic operators' static semantics (<strong>MS-VBAL 5.6.9.3</strong>): the declared type of each is the plan of its operation, made for the
/// declared types of its operands (<see cref="PlannedOperatorStaticSemantics"/>).
/// </summary>
public abstract record class BinaryArithmeticOperatorStaticSemantics : PlannedOperatorStaticSemantics;
