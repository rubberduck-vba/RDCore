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

    /// <summary>
    /// What it names is written to: it is the target of an assignment, the counter or control variable of a loop, the string a <c>Mid</c> statement replaces a
    /// part of, or the array a <c>ReDim</c> gives its dimensions. An element of an array is written through the array, which is what is flagged.
    /// </summary>
    AssignmentTarget = 1 << 8,

    /// <summary>
    /// It is an argument of a call that may take it by reference: what the procedure does with it, reads it or writes to it, is not what the expression says.
    /// An argument that is passed by value, and an index of an array, is not.
    /// </summary>
    PassedAsArgument = 1 << 9,
    /// <summary>
    /// It is an argument written with <c>ByVal</c> for a parameter that is <c>ByVal</c> already: the keyword changes nothing. <strong>MS-VBA</strong> compiles
    /// it, where <strong>MS-VBAL §5.6.13.1</strong> refuses it (RD-VBAL §5.6.13.1).
    /// </summary>
    RedundantByVal = 1 << 10,

    /// <summary>
    /// It is a call of a function, or of a property that has no <c>Property Set</c>, named as the target of a <c>Set</c> statement outside its own body, of a
    /// class whose default member has no <c>Property Set</c>: what it returns has nothing to Set, and the <c>Set</c> raises run-time error 438 (Object doesn't
    /// support this property or method) whenever it runs. <strong>MS-VBA</strong> compiles it (<strong>MS-VBAL §5.4.3.9</strong>).
    /// </summary>
    /// <remarks>
    /// A call of a type that is neither an object nor a <c>Variant</c> is a compile error as the target of a <c>Set</c> already; one of an <c>Object</c> or a
    /// <c>Variant</c> fails or not by the value it returns, which only an evaluation of it can tell.
    /// </remarks>
    SetHasNoDefaultMemberToSet = 1 << 11,

    /// <summary>
    /// It is an addition, a subtraction or a multiplication of <c>Byte</c>, <c>Integer</c> or <c>Long</c> values, one of which is <c>Abs</c>, <c>Int</c> or
    /// <c>Fix</c> of a number: MS-VBA types that call as the number's type, and the operation overflows (error 6) where MS-VBAL, which declares the call
    /// <c>As Variant</c>, has a <c>Variant</c> operation widen to a larger type (<see cref="Static.ArgumentTypedFunctions"/>).
    /// </summary>
    OverflowsWhereVariantWidens = 1 << 12,

    /// <summary>
    /// It is a name of a reserved function of the library written with a type character that does not match the function's declared type (<c>Len$</c>,
    /// <c>CVar$</c>, <c>Abs%</c>): <strong>MS-VBAL §5.6.10</strong> makes the name invalid, and <strong>MS-VBA</strong> ignores the character.
    /// </summary>
    IgnoredTypeCharacter = 1 << 13,

    /// <summary>
    /// It is an object of a class that has no default member, where a value is expected: the value of a <c>Let</c> assignment, an operand of an operator other
    /// than <c>Is</c>, an argument of a parameter declared with a value type. <strong>MS-VBAL §5.5.1.1</strong> makes it statically invalid; <strong>MS-VBA</strong>
    /// compiles it, and raises run-time error 438 (Object doesn't support this property or method) whenever it runs, or 91 when the object is <c>Nothing</c>.
    /// </summary>
    ValueOfObjectWithNoDefaultMember = 1 << 14,

    /// <summary>
    /// It is the <c>Empty</c> or the <c>Null</c> literal, Let-assigned to an array: a variable, or the result of a function, declared as one. Its declared
    /// type is <c>Variant</c> (<strong>MS-VBAL §3.3.5.2</strong>), which is valid to Let-coerce to an array; the value it holds raises run-time error 13
    /// (Type mismatch) whenever it runs, as <strong>MS-VBA</strong> has it.
    /// </summary>
    EmptyLetToArray = 1 << 15,

    /// <summary>
    /// A call of <c>IsMissing</c> (<strong>MS-VBAL §6.1.2.7.1.6</strong>) of a parameter that can never be missing, so that the call is always <c>False</c>:
    /// a parameter that is not a <c>Variant</c>, which an omitted argument does not leave missing, or an <c>Optional</c> <c>Variant</c> with a default
    /// value, which an omitted argument gives that value. A <c>Variant</c> parameter with no default can be missing, required or not (an omitted argument
    /// passed on to it), and so can a <c>ParamArray</c> with no elements, as <strong>MS-VBA</strong> has it.
    /// </summary>
    IsMissingAlwaysFalse = 1 << 16,

    /// <summary>
    /// A name of a document module, written where <strong>MS-VBA</strong>'s host makes it the document (<c>Sheet1.Range</c>, <c>ThisWorkbook.Save</c>, in
    /// another module): RD-VBA hosts no document, and the name stands for nothing - a variable that is not declared, or one that is declared by being named
    /// (<strong>MS-VBAL §5.6.10</strong>). Code that works with the document holds it in a variable of the host's class, from an application the program
    /// automates (<see cref="DocumentModuleFact"/>).
    /// </summary>
    HostedDocumentName = 1 << 17,
}
