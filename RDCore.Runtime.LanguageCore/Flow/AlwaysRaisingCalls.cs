using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Flow;

/// <summary>
/// The calls of a procedure that never complete: a call of the <c>Raise</c> method of the library's <c>Err</c> object, which generates a run-time error
/// (<strong>MS-VBAL §6.1.3.2.1.2</strong>) the way an <c>Error</c> statement does (<strong>MS-VBAL §5.4.4.3</strong>). Control leaves such a call only by the
/// error it raises.
/// </summary>
/// <remarks>
/// The call is told by what its callee binds to, so a <c>Raise</c> of another object, or of a variable named <c>Err</c> of the workspace's own class, is a call
/// like any other.
/// </remarks>
internal static class AlwaysRaisingCalls
{
    private const string Raise = "ErrObject.Raise";

    public static IReadOnlySet<int> Of(InstructionList instructions, ProcedureSemanticModel model)
        => instructions.Items
            .Where(instruction => instruction is { Kind: InstructionKind.Simple, Node: CallStatementNode call } && Raises(call.Callee, model))
            .Select(instruction => instruction.Offset)
            .ToHashSet();

    // `Err.Raise 5`, and `Call Err.Raise(5)`, whose callee is the call of the method with its arguments.
    private static bool Raises(ExpressionNode callee, ProcedureSemanticModel model)
        => IsRaise(callee, model) || callee is IndexExpressionNode { Callee: { } method } && IsRaise(method, model);

    private static bool IsRaise(SyntaxNode node, ProcedureSemanticModel model)
        => model.Expressions.TryGetValue(node.Identity, out var fact)
           && fact is { Error: null, Binding: { } binding }
           && string.Equals(SymbolUri.QualifiedNameOf(binding.Uri), Raise, StringComparison.OrdinalIgnoreCase);
}
