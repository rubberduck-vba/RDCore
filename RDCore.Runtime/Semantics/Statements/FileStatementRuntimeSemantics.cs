using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// <strong>MS-VBAL §5.4.5</strong> File statements: the ones that associate and disassociate a file number.
/// </summary>
/// <remarks>
/// 🚧 <c>Open</c>, <c>Close</c> and <c>Reset</c> only. The statements that move data — <c>Print #</c>,
/// <c>Write #</c>, <c>Input #</c>, <c>Line Input #</c>, <c>Put</c>, <c>Get</c> — and the ones that position or
/// lock a channel — <c>Seek</c>, <c>Width</c>, <c>Lock</c>, <c>Unlock</c> — come next, and all of them go
/// through the same <see cref="IFileChannels"/> this one opens against.
/// </remarks>
/// <param name="Expressions">Evaluates the path, file-number and record-length expressions.</param>
/// <param name="Printing">Applies <strong>§5.4.5.8</strong>'s output rules, whichever target they are aimed at.</param>
/// <param name="Numbers">Coerces them to the types the statement's own clauses declare.</param>
/// <param name="Strings">Coerces the path expression to <c>String</c>, which the specification requires of it.</param>
public sealed record class FileStatementRuntimeSemantics(
    RuntimeExpressionEvaluator Expressions,
    PrintOutputEvaluator Printing,
    VBNumericLetCoercionTypeRuntimeSemantics Numbers,
    VBStringLetCoercionRuntimeSemantics Strings)
{
    /// <summary>
    /// Executes an <c>Open</c> statement (<strong>MS-VBAL §5.4.5.1</strong>).
    /// </summary>
    /// <param name="session">The session whose channels the statement opens against.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="open">The statement.</param>
    public RuntimeExecutionOutcome ExecuteOpen(
        IRuntimeSession session, RuntimeEvaluationContext context, OpenStatementNode open)
    {
        if (!TryEvaluateString(session, context, open.PathName, out var path, out var pathFailure))
        {
            return pathFailure;
        }

        if (!TryEvaluateInteger(session, context, open.FileNumber, out var fileNumber, out var numberFailure))
        {
            return numberFailure;
        }

        // "If there is no <mode-clause> the effect is as if there were a <mode-clause> where <mode> is keyword
        // Random", and the access follows from the mode when no <access-clause> says otherwise.
        var mode = open.Mode ?? VBFileMode.Random;
        var access = open.Access ?? FileStatementAccess.ImpliedAccess(mode);
        var @lock = open.Lock ?? VBFileLockMode.Shared;

        if (!TryEvaluateRecordLength(session, context, open, out var recordLength, out var lengthFailure))
        {
            return lengthFailure;
        }

        return session.Files.TryOpen(fileNumber, path, mode, access, @lock, recordLength) is { } error
            ? Failed(error, open, $"Open \"{path}\" For {mode} As #{fileNumber}")
            : RuntimeExecutionOutcome.Next;
    }

    /// <summary>
    /// Executes a <c>Print #</c> statement (<strong>MS-VBAL §5.4.5.8</strong>).
    /// </summary>
    /// <remarks>
    /// The output rules are <see cref="PrintOutputEvaluator"/>'s, unchanged - the print zones, the leading
    /// space on a positive number, <c>Spc</c>, <c>Tab</c>, and a trailing <c>;</c> holding the line open. Only
    /// where the characters go differs, so only that is passed.
    /// </remarks>
    /// <param name="session">The session whose channel the statement writes to.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="print">The statement.</param>
    public RuntimeExecutionOutcome ExecutePrint(
        IRuntimeSession session, RuntimeEvaluationContext context, PrintStatementNode print)
    {
        if (print.FileNumber is null)
        {
            // the object-relative bare form invokes the enclosing form or report's own Print member, and
            // neither forms nor reports exist. TODO when a document module can be a Print target.
            return RuntimeExecutionOutcome.InternalError;
        }

        if (!TryResolveChannel(session, context, print.FileNumber, print.Token, print, out var channel, out var failure))
        {
            return failure;
        }

        return Printing.Execute(session, context, print.Items, channel!.Output);
    }

    // "An error (number 52, 'Bad file name or number') is raised if the file number value... is not a
    // currently-open file number", and a statement the channel's own mode and access do not permit is
    // error 54 - which MS-VBAL 5.4.5.1 states once, as a table, for every file statement.
    private bool TryResolveChannel(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode fileNumber, string statement,
        StatementNode node, out IFileChannel? channel, out RuntimeExecutionOutcome failure)
    {
        channel = null;
        if (!TryEvaluateInteger(session, context, fileNumber, out var number, out failure))
        {
            return false;
        }

        if (!session.Files.TryGet(number, out channel))
        {
            failure = Failed(VBRuntimeErrorId.BadFileNameOrNumber, node, $"#{number} is not open");
            return false;
        }

        if (!FileStatementAccess.IsValid(statement, channel!.Mode, channel.Access))
        {
            failure = Failed(VBRuntimeErrorId.BadFileMode, node,
                $"{statement} is not valid on #{number}, opened For {channel.Mode} Access {channel.Access}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Executes a <c>Close</c> or <c>Reset</c> statement (<strong>MS-VBAL §5.4.5.2</strong>).
    /// </summary>
    /// <remarks>
    /// A <c>Close</c> with no file number closes every channel, which is exactly what <c>Reset</c> does — the
    /// specification gives them one section because they are one behaviour with two spellings. Closing a file
    /// number that is not open is <em>not</em> an error: VBA's <c>Close</c> is idempotent, which is what makes
    /// it safe in an error handler.
    /// </remarks>
    /// <param name="session">The session whose channels the statement closes.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, whose inputs are the file numbers to close, if any.</param>
    public RuntimeExecutionOutcome ExecuteClose(
        IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode statement)
    {
        if (statement.Inputs.IsDefaultOrEmpty || statement.Token.Equals(Tokens.Reset, StringComparison.OrdinalIgnoreCase))
        {
            session.Files.CloseAll();
            return RuntimeExecutionOutcome.Next;
        }

        foreach (var input in statement.Inputs.OfType<ExpressionNode>())
        {
            if (!TryEvaluateInteger(session, context, input, out var fileNumber, out var failure))
            {
                return failure;
            }

            session.Files.Close(fileNumber);
        }

        return RuntimeExecutionOutcome.Next;
    }

    // "The expression in a <len-clause> production MUST evaluate to a data value that is Let-coercible to
    // declared type Integer in the inclusive range 1 to 32,767. The <len-clause> is ignored if <mode> is
    // Binary" - so a Binary open does not even evaluate it, and an absent one is 0.
    private bool TryEvaluateRecordLength(
        IRuntimeSession session, RuntimeEvaluationContext context, OpenStatementNode open,
        out int recordLength, out RuntimeExecutionOutcome failure)
    {
        recordLength = 0;
        failure = RuntimeExecutionOutcome.Next;

        if (open.RecordLength is null || open.Mode is VBFileMode.Binary)
        {
            return true;
        }

        if (!TryEvaluateInteger(session, context, open.RecordLength, out recordLength, out failure))
        {
            return false;
        }

        if (recordLength is < 1 or > short.MaxValue)
        {
            failure = Failed(VBRuntimeErrorId.BadRecordLength, open, $"Len = {recordLength}");
            return false;
        }

        return true;
    }

    private bool TryEvaluateString(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression,
        out string value, out RuntimeExecutionOutcome failure)
    {
        value = string.Empty;
        var evaluated = Expressions.Evaluate(session, expression, context);
        if (!evaluated.IsSuccess)
        {
            failure = ToFailure(evaluated);
            return false;
        }

        var coerced = Strings.EvaluateLetCoercion(session.Symbols.Resolver, expression, new()
        {
            NodeId = expression.Identity,
            SourceValue = evaluated.Result!,
            DestinationTypeDesc = new(VBStringType.TypeInfo),
        });

        if (!coerced.IsSuccess)
        {
            failure = RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
            return false;
        }

        value = coerced.Result!.Handle.Value.BoxedValue as string ?? string.Empty;
        failure = RuntimeExecutionOutcome.Next;
        return true;
    }

    private bool TryEvaluateInteger(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression,
        out int value, out RuntimeExecutionOutcome failure)
    {
        value = 0;
        var evaluated = Expressions.Evaluate(session, expression, context);
        if (!evaluated.IsSuccess)
        {
            failure = ToFailure(evaluated);
            return false;
        }

        var coerced = Numbers.EvaluateLetCoercion(session.Symbols.Resolver, expression, new()
        {
            NodeId = expression.Identity,
            SourceValue = evaluated.Result!,
            DestinationTypeDesc = new(VBIntegerType.TypeInfo),
        });

        if (!coerced.IsSuccess)
        {
            failure = RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
            return false;
        }

        value = Convert.ToInt32(coerced.Result!.Handle.Value.BoxedValue);
        failure = RuntimeExecutionOutcome.Next;
        return true;
    }

    private static RuntimeExecutionOutcome ToFailure(SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult result)
        => result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);

    private static RuntimeExecutionOutcome Failed(VBRuntimeErrorId error, StatementNode statement, string detail)
        => RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(error, statement.SourceLocation, detail));
}
