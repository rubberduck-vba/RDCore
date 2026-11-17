using RDCore.Runtime.Execution.Frames;
using RDCore.Runtime.LanguageCore.Binding;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Statements;

namespace RDCore.Runtime.Compiled;

// what compiled code does of a With block.
internal sealed partial class ProcedureAnalysis
{
    // MS-VBAL §5.4.2.21: the With statement of a block of an object compiled code reads - an object variable of the activation or Me, an object field of
    // one of its records, a New of a class, or a call that returns an object - the block holding the reference it read, as the interpreter holds it
    // (CompiledActivation.TryWith). A record, a Variant and anything else is the interpreter's statement, which evaluates the block's object and holds it in
    // the frame: what compiled code did not read gives no reason but the statement's own.
    private WithStep? WithOf(Instruction instruction)
    {
        if (instruction is not { Node: WithStatementNode with, End: not null })
        {
            return null;
        }

        var mark = _reasons.Count;
        var target = Bind(with.WithExpression);
        WithStep? step = null;
        if (ObjectFieldOf(target) is { } field)
        {
            step = new WithStep(instruction.Offset, new ObjectFieldNode(field));
        }
        else if (_nesting is not null && ObjectCallOf(target) is { } call)
        {
            step = new WithStep(instruction.Offset, Source: null) { SourceCall = call };
        }
        else if (_nesting is not null && target is BoundNew created && KnownOperands.IndexOf(_nesting, created.Expression) is >= 0 and var syntax)
        {
            _news.Add(created);
            step = new WithStep(instruction.Offset, Source: null) { SourceNew = new NewNode(_news.Count - 1, syntax) };
        }
        else if (target is BoundVariable or BoundInstance && TrySetSource(target, out var slot) && slot is { } held)
        {
            step = new WithStep(instruction.Offset, new ObjectSlotOperand(held));
        }

        if (step is null)
        {
            _reasons.RemoveRange(mark, _reasons.Count - mark);
            return null;
        }

        // the block holds its object in the frame, which lets go of it as the activation returns when the block is left before its End With.
        NeedsFrame = true;
        return step;
    }

    // MS-VBAL §5.4.2.21: the End With of the block opened at the With statement it matches, which compiled code does (CompiledActivation.TryEndWith).
    private static EndWithStep? EndWithOf(Instruction instruction)
        => instruction.Matching is { } opener ? new EndWithStep(instruction.Offset, opener) : null;
}

/// <summary>
/// A <c>With</c> statement of an object (MS-VBAL §5.4.2.21): the block opened at <see cref="CompiledStep.Offset"/> holds the reference held in
/// <see cref="Source"/>, or the object <see cref="SourceCall"/> returns, or the one <see cref="SourceNew"/> makes, until its <c>End With</c>
/// (<c>CompiledActivation.TryWith</c>).
/// </summary>
internal sealed record class WithStep(int Offset, ObjectOperand? Source) : CompiledStep(Offset)
{
    /// <summary>
    /// The call whose object the block holds: made first, and what it returns held by the statement's temporaries until the statement is over.
    /// </summary>
    public ObjectCallNode? SourceCall { get; init; }

    /// <summary>
    /// The <c>New</c> whose object the block holds: made first, and held by the statement's temporaries until the statement is over.
    /// </summary>
    public NewNode? SourceNew { get; init; }
}

/// <summary>
/// An <c>End With</c> (MS-VBAL §5.4.2.21): the block opened at <see cref="Opener"/> has no object from here on, and what it held is let go of
/// (<c>CompiledActivation.TryEndWith</c>).
/// </summary>
internal sealed record class EndWithStep(int Offset, int Opener) : CompiledStep(Offset);
