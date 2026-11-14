using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;

namespace RDCore.Runtime.Compiled;

// a statement the interpreter runs for compiled code, as a call compiled code makes.
internal sealed partial class ProcedureAnalysis
{
    private readonly List<Ineligibility> _supportCalls = [];

    /// <summary>
    /// What the interpreter runs of a compiled procedure as a support call, and why: each <c>Set</c> or call statement compiled code does not make itself,
    /// which is no reason for the procedure to be interpreted. A statement that gave no reason is described as itself: a call by what it calls.
    /// </summary>
    public IReadOnlyList<Ineligibility> SupportCalls => _supportCalls;

    // the statement of instruction, which compiled code does not run, as a support call (CompiledActivation.RunStatement): the reasons it gave since mark are
    // not the procedure's, but the statement's, at the statement when they have no location of their own. Inside a With block it is evaluated against the
    // object the block holds, as the interpreter evaluates it (ContextAt).
    private SupportCallStep SupportCallOf(Instruction instruction, int mark)
    {
        var location = instruction.Node?.SourceLocation;
        _supportCalls.AddRange(_reasons.Count > mark
            ? _reasons.Skip(mark).Select(reason => reason.Location is null ? reason with { Location = location } : reason)
            : [StatementOf(instruction, location)]);
        _reasons.RemoveRange(mark, _reasons.Count - mark);
        return new SupportCallStep(instruction.Offset);
    }

    // a support call that gave no reason, as the report names it: a call by the name of what it calls, as RefusedCall names it; a Set, or a statement of a
    // keyword, by its keyword.
    private static Ineligibility StatementOf(Instruction instruction, SourceLocation? location) => instruction.Node switch
    {
        CallStatementNode { Callee: SimpleNameExpressionNode name } => new Ineligibility(IneligibilityKind.Call, name.IdentifierName, location),
        CallStatementNode { Callee: MemberAccessExpressionNode member } => new Ineligibility(IneligibilityKind.Call, member.Member.IdentifierName, location),
        AssignmentStatementNode { Kind: AssignmentKind.Set } => new Ineligibility(IneligibilityKind.Statement, nameof(AssignmentKind.Set), location),
        KeywordStatementNode keyword => new Ineligibility(IneligibilityKind.Statement, keyword.Token, location),
        var node => new Ineligibility(IneligibilityKind.Statement, node?.GetType().Name ?? instruction.Kind.ToString(), location),
    };
}

/// <summary>
/// The statement at <see cref="CompiledStep.Offset"/> (a <c>Set</c>, or a call statement) run by the interpreter as a support call, every variable written
/// into the frame before it and read back after it.
/// </summary>
internal sealed record class SupportCallStep(int Offset) : CompiledStep(Offset);
