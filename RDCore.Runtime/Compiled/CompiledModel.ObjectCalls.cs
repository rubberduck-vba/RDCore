namespace RDCore.Runtime.Compiled;

// a call of a function or a Property Get that returns an object (CompiledCallee.ReturnsObject), inside an expression, whose object compiled code calls a
// member of or reads a public variable of (`S.Pos.Y`, `Make().Pos.Move 1`): made as a CallNode is (of a function of a standard module, or of a member of an
// object variable (Receiver), of an object field of a record (ReceiverField), of the object of a With block (WithAt) or of the object another such call
// returns (ReceiverCall)), with arguments as a CallStep's. What it returns is held by the statement's temporaries (ObjectReferences.HoldTemporarily) until
// the statement is over. Once it is made, the instruction is handed to the interpreter in the middle of it with the object known (Syntax): the call is not
// made again.
internal sealed record class ObjectCallNode(int Callee, IReadOnlyList<PayloadNode> Arguments, int? Receiver, int Syntax)
{
    public int? WithAt { get; init; }

    public FieldPath? ReceiverField { get; init; }

    public ObjectCallNode? ReceiverCall { get; init; }
}

// a New of a class of the workspace that a Set is given (MS-VBAL §5.6.8): the object made as the interpreter makes it (RuntimeExpressionEvaluator.CreateObject),
// its Initialize run before the Set is given it (MS-VBAL §5.3.1.10), and held by the statement's temporaries until the statement is over. Made: the number
// of the New (ProcedureAnalysis.News). Once it is made, the instruction is handed to the interpreter in the middle of it with the object known (Syntax):
// the object is not made again.
internal sealed record class NewNode(int Made, int Syntax);
