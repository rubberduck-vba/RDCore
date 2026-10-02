namespace RDCore.SDK.Semantics.Flags;

/// <summary>
/// The semantic flags of an expression that yields a value: the facts of how it is bound and what it is written as.
/// </summary>
[Flags]
public enum ValueExpressionSemanticFlags
{
    /// <summary>
    /// The expression is a literal: its value is what is written.
    /// </summary>
    Literal = 1 << 0,

    /// <summary>
    /// The member it names is not one its owner's declared type says it has, and is bound when the expression runs: the owner is a <c>Variant</c> or an <c>Object</c>.
    /// </summary>
    LateBound = 1 << 1,

    /// <summary>
    /// It indexes an object, which is a call of the default member of its class: <c>c(1)</c> is <c>c.Item(1)</c>.
    /// </summary>
    DefaultMember = 1 << 2,

    /// <summary>
    /// It is written relative to the object of an enclosing <c>With</c> block: <c>.Member</c> or <c>!Member</c>.
    /// </summary>
    WithBlockRelative = 1 << 3,

    /// <summary>
    /// It is a dictionary access, <c>owner!Member</c>: a call of the default member of the owner with the name of the member as its argument.
    /// </summary>
    DictionaryAccess = 1 << 4,

    /// <summary>
    /// It invokes a procedure, however it is written: a name with no argument list, or an index expression on a procedure.
    /// </summary>
    ProcedureCall = 1 << 5,

    /// <summary>
    /// The name is written in a different case than the declaration it refers to.
    /// </summary>
    CaseMismatch = 1 << 6,

    /// <summary>
    /// It is the callee of a <c>Call</c> statement written with the <c>Call</c> keyword (<see cref="RDCore.SDK.Model.AST.Statements.CallStatementNode.IsExplicitCall"/>),
    /// the obsolete form of the statement.
    /// </summary>
    ExplicitCallKeyword = 1 << 7,
}
