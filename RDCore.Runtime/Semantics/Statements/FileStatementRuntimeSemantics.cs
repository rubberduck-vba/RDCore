using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.LetCoercion;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Semantics.Statements;

/// <summary>
/// <strong>MS-VBAL §5.4.5</strong> File statements: the ones that associate and disassociate a file number,
/// and the ones that move data through one.
/// </summary>
/// <remarks>
/// Every statement of the section, all of them through the same <see cref="IFileChannels"/> this one opens
/// against: <c>Open</c>, <c>Close</c> and <c>Reset</c>, which associate a file number and disassociate it;
/// <c>Print #</c>, <c>Write #</c>, <c>Line Input #</c> and <c>Input #</c>, which move characters;
/// <c>Put</c> and <c>Get</c>, which move records of bytes; and <c>Seek</c>, <c>Width</c>, <c>Lock</c> and
/// <c>Unlock</c>, which position a channel or restrict it.
/// <para>
/// 🚧 The one thing still missing is a <c>Put</c> or <c>Get</c> whose data is a UDT — see
/// <see cref="RDCore.Runtime.Execution.Files.RecordDataFormat"/>'s own note.
/// </para>
/// </remarks>
/// <param name="Expressions">Evaluates the path, file-number and record-length expressions.</param>
/// <param name="Printing">Applies <strong>§5.4.5.8</strong>'s output rules, whichever target they are aimed at.</param>
/// <param name="Writing">Applies <strong>§5.4.5.9</strong>'s record format, whichever target it is aimed at.</param>
/// <param name="Numbers">Coerces them to the types the statement's own clauses declare.</param>
/// <param name="Strings">Coerces the path expression to <c>String</c>, which the specification requires of it.</param>
/// <param name="Assignments">Let-assigns what a reading statement read into the variable it names.</param>
/// <param name="Reading">Applies <strong>§5.4.5.10</strong>'s input-list rules to the fields of a record.</param>
public sealed record class FileStatementRuntimeSemantics(
    RuntimeExpressionEvaluator Expressions,
    PrintOutputEvaluator Printing,
    WriteOutputEvaluator Writing,
    VBNumericLetCoercionTypeRuntimeSemantics Numbers,
    VBStringLetCoercionRuntimeSemantics Strings,
    LetAssignmentEvaluator Assignments,
    InputListEvaluator Reading)
{
    /// <summary>
    /// The lowest file number a file number may take (<strong>MS-VBAL §5.4.5</strong>).
    /// </summary>
    public const int MinFileNumber = 1;

    /// <summary>
    /// The highest file number a file number may take (<strong>MS-VBAL §5.4.5</strong>).
    /// </summary>
    public const int MaxFileNumber = 511;

    /// <summary>
    /// The widest line a <c>Width</c> statement may ask for (<strong>MS-VBAL §5.4.5.7</strong>).
    /// </summary>
    public const int MaxLineWidth = 255;

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

        if (!TryEvaluateFileNumber(session, context, open.FileNumber, open, out var fileNumber, out var numberFailure))
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
    /// Executes a <c>Print #</c> or <c>Write #</c> statement (<strong>MS-VBAL §5.4.5.8-9</strong>).
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

        return print.Token.Equals(Tokens.Write, StringComparison.OrdinalIgnoreCase)
            ? Writing.Execute(session, context, print.Items, channel!.Output)
            : Printing.Execute(session, context, print.Items, channel!.Output);
    }

    /// <summary>
    /// Executes a <c>Line Input #</c> statement (<strong>MS-VBAL §5.4.5.6</strong>).
    /// </summary>
    /// <remarks>
    /// Reads "the sequence of bytes starting at the current file-pointer-position... through the last byte of
    /// the current line (but not including the line termination sequence)" and Let-assigns it into the
    /// variable the statement names. A line ended by the end of the file is still a line; a read that starts
    /// there is error <c>62</c>.
    /// </remarks>
    /// <param name="session">The session whose channel the statement reads from.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, whose inputs are the file number and the variable to assign.</param>
    public RuntimeExecutionOutcome ExecuteLineInput(
        IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode statement)
    {
        if (statement.Inputs is not [ExpressionNode fileNumber, ExpressionNode variable])
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (!TryResolveChannel(session, context, fileNumber, Tokens.LineInput, statement, out var channel, out var failure))
        {
            return failure;
        }

        // "If the file is empty or there are no characters after file-pointer-position, then runtime error 62
        // ('Input past end of file') is raised" - which an empty line read from a file that does have one is
        // not, so the channel tells the two apart rather than this reading an empty string as either.
        if (channel!.Input.ReadLine() is not { } line)
        {
            return Failed(VBRuntimeErrorId.InputPastEndOfFile, statement,
                $"#{channel.FileNumber} has no characters left to read");
        }

        // "The String data value is Let-assigned into <variable-name>". The variable is also where a coercion
        // error is reported, there being no source expression in this statement to report one at.
        return Assignments.Assign(session, context, statement, variable, variable, new VBStringValue(line));
    }

    /// <summary>
    /// Executes an <c>Input #</c> statement (<strong>MS-VBAL §5.4.5.10</strong>).
    /// </summary>
    /// <remarks>
    /// The rules are <see cref="InputListEvaluator"/>'s; this resolves the channel they read from, which is
    /// the one thing every file statement does the same way.
    /// </remarks>
    /// <param name="session">The session whose channel the statement reads from.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, whose inputs are the file number and then the input list.</param>
    public RuntimeExecutionOutcome ExecuteInput(
        IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode statement)
    {
        if (statement.Inputs is not [ExpressionNode fileNumber, .. var rest] || rest.Length == 0)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var variables = rest.OfType<ExpressionNode>().ToArray();
        if (variables.Length != rest.Length)
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (!TryResolveChannel(session, context, fileNumber, Tokens.Input, statement, out var channel, out var failure))
        {
            return failure;
        }

        return Reading.Execute(session, context, statement, variables, channel!.Input);
    }

    /// <summary>
    /// Executes a <c>Seek</c> statement (<strong>MS-VBAL §5.4.5.3</strong>).
    /// </summary>
    /// <remarks>
    /// The position is in records on a <see cref="VBFileMode.Random"/> channel and in bytes otherwise, which
    /// is the channel's own business — this evaluates the expression and hands the number over.
    /// </remarks>
    /// <param name="session">The session whose channel the statement repositions.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, whose inputs are the file number and the position.</param>
    public RuntimeExecutionOutcome ExecuteSeek(
        IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode statement)
    {
        if (statement.Inputs is not [ExpressionNode fileNumber, ExpressionNode position])
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (!TryResolveChannel(session, context, fileNumber, Tokens.Seek, statement, out var channel, out var failure))
        {
            return failure;
        }

        // "The new file position is the evaluated value of <position> Let-coerced to declared type Long."
        if (!TryEvaluateLong(session, context, position, out var newPosition, out var positionFailure))
        {
            return positionFailure;
        }

        return channel!.Seek(newPosition) is { } error
            ? Failed(error, statement, $"Seek #{channel.FileNumber}, {newPosition}")
            : RuntimeExecutionOutcome.Next;
    }

    /// <summary>
    /// Executes a <c>Width</c> statement (<strong>MS-VBAL §5.4.5.7</strong>).
    /// </summary>
    /// <remarks>
    /// Sets the channel's maximum line length, which <c>Print #</c> and <c>Write #</c> wrap at.
    /// <c>Width #n, 0</c> returns it to having no maximum, and on a <see cref="VBFileMode.Binary"/> or
    /// <see cref="VBFileMode.Random"/> channel the statement "has no effect upon the file" — it is still valid
    /// there, so this is a no-op and not an error.
    /// </remarks>
    /// <param name="session">The session whose channel the statement sets the width of.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, whose inputs are the file number and the line width.</param>
    public RuntimeExecutionOutcome ExecuteWidth(
        IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode statement)
    {
        if (statement.Inputs is not [ExpressionNode fileNumber, ExpressionNode lineWidth])
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        if (!TryResolveChannel(session, context, fileNumber, Tokens.Width, statement, out var channel, out var failure))
        {
            return failure;
        }

        // "The line width is the evaluated value of <line-width> Let-coerced to declared type Integer."
        if (!TryEvaluateInteger(session, context, lineWidth, out var width, out var widthFailure))
        {
            return widthFailure;
        }

        // "If Line width is less than 0 or greater than 255 an error (number 5, 'Invalid procedure call or
        // argument') is raised."
        if (width is < 0 or > MaxLineWidth)
        {
            return Failed(VBRuntimeErrorId.InvalidProcedureCallOrArgument, statement, $"Width #{channel!.FileNumber}, {width}");
        }

        if (channel!.Mode is not (VBFileMode.Binary or VBFileMode.Random))
        {
            channel.Output.MaxLineLength = width;
        }

        return RuntimeExecutionOutcome.Next;
    }

    /// <summary>
    /// Executes a <c>Lock</c> or <c>Unlock</c> statement (<strong>MS-VBAL §5.4.5.4-5</strong>).
    /// </summary>
    /// <remarks>
    /// One method for both because the specification gives <c>Unlock</c> "the static semantics for
    /// <c>lock-statement</c>" and then the same runtime rules about its range — only what happens to the lock
    /// at the end differs, which is the channel's business rather than this one's.
    /// </remarks>
    /// <param name="session">The session whose channel the statement locks or unlocks.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, whose range is <c>null</c> at either end it did not declare.</param>
    public RuntimeExecutionOutcome ExecuteLock(
        IRuntimeSession session, RuntimeEvaluationContext context, FileLockStatementNode statement)
    {
        var isUnlock = statement.Token.Equals(Tokens.Unlock, StringComparison.OrdinalIgnoreCase);
        if (!TryResolveChannel(session, context, statement.FileNumber, statement.Token, statement, out var channel, out var failure))
        {
            return failure;
        }

        if (statement.IsEntireFile)
        {
            return Applied(channel!, FileRecordRange.EntireFile, isUnlock, statement);
        }

        // "If there is no <start-record-number> the effect is as if <start-record-number> consisted of the
        // integer number token 1" - which is why the node leaves it absent rather than synthesizing one.
        var start = 1L;
        if (statement.StartRecord is { } startExpression
            && !TryEvaluateLong(session, context, startExpression, out start, out var startFailure))
        {
            return startFailure;
        }

        // no <end-record-number> means the range is the start record alone: `Lock #1, 5` locks record 5, where
        // `Lock #1, To 5` locks records 1 through 5.
        var end = start;
        if (statement.EndRecord is { } endExpression
            && !TryEvaluateLong(session, context, endExpression, out end, out var endFailure))
        {
            return endFailure;
        }

        return Applied(channel!, new FileRecordRange(start, end), isUnlock, statement);
    }

    private static RuntimeExecutionOutcome Applied(
        IFileChannel channel, FileRecordRange range, bool isUnlock, FileLockStatementNode statement)
    {
        var error = isUnlock ? channel.UnlockRange(range) : channel.LockRange(range);
        return error is { } raised
            ? Failed(raised, statement, $"{statement.Token} #{channel.FileNumber}, {range.Start} To {range.End}")
            : RuntimeExecutionOutcome.Next;
    }

    /// <summary>
    /// Executes a <c>Put</c> statement (<strong>MS-VBAL §5.4.5.11</strong>).
    /// </summary>
    /// <remarks>
    /// The record format is <see cref="RecordDataFormat"/>'s. What this adds is the positioning — an absent
    /// record number means "the current file-pointer-position" — and the record-length check a
    /// <see cref="VBFileMode.Random"/> channel makes afterwards.
    /// </remarks>
    /// <param name="session">The session whose channel the statement writes to.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, whose inputs are the file number, the record number if it
    /// declared one, and the data expression.</param>
    public RuntimeExecutionOutcome ExecutePut(
        IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode statement)
    {
        if (!TryResolveRecord(session, context, statement, Tokens.Put, out var record, out var failure))
        {
            return failure;
        }

        var evaluated = Expressions.Evaluate(session, record.Data, context);
        if (!evaluated.IsSuccess)
        {
            return ToFailure(evaluated);
        }

        var isVariant = evaluated.Result! is VBVariantValue;
        if (!record.Channel.TryWriteRecord(evaluated.Result!, isVariant, out var written))
        {
            // the format's ERROR rows: an object, a UDT, or a Variant holding one. A UDT is the case that is
            // specified and not implemented, and RecordDataFormat's own TODO names it.
            return RuntimeExecutionOutcome.InternalError;
        }

        // "If the number of bytes written is more than the specified <rec-length>, an error is generated (#59,
        // 'Bad record length')" - and if it is less, "the remaining bytes are written to the file are
        // undefined", so nothing pads them.
        return record.Channel.Mode is VBFileMode.Random && record.Channel.RecordLength > 0 && written > record.Channel.RecordLength
            ? Failed(VBRuntimeErrorId.BadRecordLength, statement, $"{written} bytes into a {record.Channel.RecordLength}-byte record")
            : RuntimeExecutionOutcome.Next;
    }

    /// <summary>
    /// Executes a <c>Get</c> statement (<strong>MS-VBAL §5.4.5.12</strong>).
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="ExecutePut"/>: how many bytes to read follows from the declared type of the
    /// variable being read into, or — for a <c>Variant</c> — from the type descriptor the record itself
    /// carries.
    /// </remarks>
    /// <param name="session">The session whose channel the statement reads from.</param>
    /// <param name="context">The evaluation context of the statement.</param>
    /// <param name="statement">The statement, whose inputs are the file number, the record number if it
    /// declared one, and the variable to assign.</param>
    public RuntimeExecutionOutcome ExecuteGet(
        IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode statement)
    {
        if (!TryResolveRecord(session, context, statement, Tokens.Get, out var record, out var failure))
        {
            return failure;
        }

        if (!Assignments.TryResolveTarget(session, context, record.Data, out var symbol))
        {
            return RuntimeExecutionOutcome.InternalError;
        }

        var declaredType = symbol is ITypedSymbol { ResolvedType: var resolved } ? resolved : VBVariantType.TypeInfo;

        // a Binary-mode String is as long as the variable already is, so what it currently holds is part of
        // deciding how much of the file to read.
        var currentLength = session.Symbols.Resolver.GetValue(symbol!).Value.BoxedValue is string text ? text.Length : 0;

        if (!record.Channel.TryReadRecord(declaredType, currentLength, out var value))
        {
            return Failed(VBRuntimeErrorId.InputPastEndOfFile, statement, $"reading into {symbol!.Name}");
        }

        return Assignments.Assign(session, context, statement, symbol!, record.Data, record.Data, value!);
    }

    // Put and Get share their whole preamble: the channel, then the position. "If no <record-number> is
    // specified, the effect is as if <record-number> is the current file-pointer-position", which is to say
    // the channel is already where it should be and nothing seeks.
    private bool TryResolveRecord(
        IRuntimeSession session, RuntimeEvaluationContext context, KeywordStatementNode statement,
        string token, out (IFileChannel Channel, ExpressionNode Data) record, out RuntimeExecutionOutcome failure)
    {
        record = default;
        if (statement.Inputs is not [ExpressionNode fileNumber, .. var rest]
            || rest is not ([ExpressionNode] or [ExpressionNode, ExpressionNode]))
        {
            failure = RuntimeExecutionOutcome.InternalError;
            return false;
        }

        if (!TryResolveChannel(session, context, fileNumber, token, statement, out var channel, out failure))
        {
            return false;
        }

        // the record number is present only when the statement gave both it and the data; with one input left
        // it is the data, and the position stands.
        if (rest is [ExpressionNode recordNumber, _]
            && !SeekToRecord(session, context, channel!, recordNumber, statement, out failure))
        {
            return false;
        }

        record = (channel!, (ExpressionNode)rest[^1]);
        return true;
    }

    // "The file-pointer-position is updated to be exactly <record-number> number of bytes from the start of the
    // file" for Binary, and "(<record-number> * <rec-length>) number of bytes" for Random. Both go through the
    // channel's own Seek, because a record number and a file-pointer-position are the same quantity - the
    // statement says so itself when it defaults one to the other - and MS-VBAL 5.4.5.3 counts that from 1.
    private bool SeekToRecord(
        IRuntimeSession session, RuntimeEvaluationContext context, IFileChannel channel,
        ExpressionNode recordNumber, StatementNode statement, out RuntimeExecutionOutcome failure)
    {
        if (!TryEvaluateLong(session, context, recordNumber, out var position, out failure))
        {
            return false;
        }

        if (channel.Seek(position) is { } error)
        {
            failure = Failed(error, statement, $"record {position} of #{channel.FileNumber}");
            return false;
        }

        return true;
    }

    // "An error (number 52, 'Bad file name or number') is raised if the file number value... is not a
    // currently-open file number", and a statement the channel's own mode and access do not permit is
    // error 54 - which MS-VBAL 5.4.5.1 states once, as a table, for every file statement.
    private bool TryResolveChannel(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode fileNumber, string statement,
        StatementNode node, out IFileChannel? channel, out RuntimeExecutionOutcome failure)
    {
        channel = null;
        if (!TryEvaluateFileNumber(session, context, fileNumber, node, out var number, out failure))
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
            if (!TryEvaluateFileNumber(session, context, input, statement, out var fileNumber, out var failure))
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

    // MS-VBAL 5.4.5.1.1: a file number is the result of Let-coercing the expression to Integer, and "if the
    // file number value is not in the inclusive range 1 to 511 error number 52 is raised" - as is an expression
    // that will not coerce at all. Every file statement's file number goes through this, not just Open's: the
    // specification states the rule once, about file numbers, rather than per statement.
    private bool TryEvaluateFileNumber(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression, StatementNode node,
        out int fileNumber, out RuntimeExecutionOutcome failure)
    {
        if (!TryEvaluateInteger(session, context, expression, out fileNumber, out _))
        {
            failure = Failed(VBRuntimeErrorId.BadFileNameOrNumber, node, "the file number is not a number");
            return false;
        }

        if (fileNumber is < MinFileNumber or > MaxFileNumber)
        {
            failure = Failed(VBRuntimeErrorId.BadFileNameOrNumber, node,
                $"#{fileNumber} is outside the {MinFileNumber}-{MaxFileNumber} range a file number may take");
            return false;
        }

        failure = RuntimeExecutionOutcome.Next;
        return true;
    }

    private bool TryEvaluateLong(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression,
        out long value, out RuntimeExecutionOutcome failure)
    {
        value = 0;
        if (!TryEvaluateNumber(session, context, expression, VBLongType.TypeInfo, out var number, out failure))
        {
            return false;
        }

        value = Convert.ToInt64(number);
        return true;
    }

    private bool TryEvaluateInteger(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression,
        out int value, out RuntimeExecutionOutcome failure)
    {
        value = 0;
        if (!TryEvaluateNumber(session, context, expression, VBIntegerType.TypeInfo, out var number, out failure))
        {
            return false;
        }

        value = Convert.ToInt32(number);
        return true;
    }

    // every numeric clause of a file statement is "the evaluated value of <x> Let-coerced to declared type
    // <T>", differing only in T - a file number and a line width are Integer, a Seek position is Long.
    private bool TryEvaluateNumber(
        IRuntimeSession session, RuntimeEvaluationContext context, ExpressionNode expression,
        VBType destinationType, out object? value, out RuntimeExecutionOutcome failure)
    {
        value = null;
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
            DestinationTypeDesc = new(destinationType),
        });

        if (!coerced.IsSuccess)
        {
            failure = RuntimeExecutionOutcome.Error(coerced.ErrorInfo!);
            return false;
        }

        value = coerced.Result!.Handle.Value.BoxedValue;
        failure = RuntimeExecutionOutcome.Next;
        return true;
    }

    private static RuntimeExecutionOutcome ToFailure(SDK.Runtime.Shared.RuntimeSemanticsEvaluationResult result)
        => result.IsInternalError ? RuntimeExecutionOutcome.InternalError : RuntimeExecutionOutcome.Error(result.ErrorInfo!);

    private static RuntimeExecutionOutcome Failed(VBRuntimeErrorId error, StatementNode statement, string detail)
        => RuntimeExecutionOutcome.Error(VBRuntimeErrorInfo.For(error, statement.SourceLocation, detail));
}
