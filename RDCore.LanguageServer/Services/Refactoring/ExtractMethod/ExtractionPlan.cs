using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types.Abstract;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// How a variable is passed to the procedure the selection is extracted as.
/// </summary>
internal enum ExtractionPassing
{
    /// <summary>
    /// Its value is passed: the new procedure reads it, and what it writes to it does not reach the caller.
    /// </summary>
    ByVal,

    /// <summary>
    /// The variable is passed: what the new procedure writes to it reaches the caller.
    /// </summary>
    ByRef,
}

/// <summary>
/// Why a variable is passed as it is: what the preview says of it ("ByRef: written here, read after").
/// </summary>
internal enum ExtractionPassingReason
{
    /// <summary>
    /// The selection reads a value given before it, and gives it no value that is read after it.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// The selection reads a value given before it, and gives it one that is read after it, on a later iteration of a loop included.
    /// </summary>
    ReadAndWritten,

    /// <summary>
    /// The selection gives it a value that is read after it, and reads none given before: it may be the new procedure's return value instead.
    /// </summary>
    WrittenOnly,

    /// <summary>
    /// An array, which VBA passes by reference only.
    /// </summary>
    Array,

    /// <summary>
    /// A variable of a <c>Type</c>, which VBA passes by reference only.
    /// </summary>
    UserDefinedType,

    /// <summary>
    /// A fixed-length string, which is passed as a <c>String</c> by reference, so that what is written to it comes back.
    /// </summary>
    FixedLengthString,

    /// <summary>
    /// A <c>Static</c> local, which belongs to the source procedure's lifetime: it is never moved into the new one.
    /// </summary>
    Static,

    /// <summary>
    /// The source procedure's <c>ParamArray</c>, which the selection only reads: it is passed as a <c>Variant</c> that holds the array of its elements.
    /// A selection that writes its elements is refused (<see cref="ExtractionRefusalKind.ParamArrayWritten"/>).
    /// </summary>
    ParamArray,
}

/// <summary>
/// Why a selection is not extracted, once what it does is known.
/// </summary>
internal enum ExtractionRefusalKind
{
    /// <summary>
    /// The selection writes an element of the source procedure's <c>ParamArray</c>, which is a variable of its caller (<strong>MS-VBAL §5.3.1.5</strong>): a copy
    /// passed to the new procedure would lose the write.
    /// </summary>
    ParamArrayWritten,

    /// <summary>
    /// The selection uses a member of the object of a <c>With</c> block it is inside of, and the block names no variable: passing its object would evaluate the
    /// expression again (<c>With New Thing</c>, <c>With Sheet.Range("A1")</c>), where the block evaluates it once.
    /// </summary>
    WithTargetIsNotAVariable,

    /// <summary>
    /// A local <c>Const</c> declared in the selection is used after it: the declaration would move into the new procedure, where the code after the call cannot
    /// name it.
    /// </summary>
    ConstantUsedAfter,

    /// <summary>
    /// A jump from outside the selection lands in it (a <c>GoTo</c>, a <c>Resume</c> to a label, an error into a handler written in it), or control enters it other
    /// than at its first statement. The labels move with the code, and a procedure's labels are its own.
    /// </summary>
    JumpIntoTheSelection,

    /// <summary>
    /// A <c>GoSub</c> in the selection to a routine outside it. The routine can be converted to a procedure first.
    /// </summary>
    GoSubToOutside,

    /// <summary>
    /// A <c>Return</c> in the selection whose <c>GoSub</c> is outside it: the selection is part of a <c>GoSub</c> routine,
    /// which can be converted to a procedure first.
    /// </summary>
    ReturnOfAGoSubOutside,

    /// <summary>
    /// An <c>On Error</c> in the selection leaves the error handling of the source procedure other than the selection found it:
    /// in the new procedure it would be the new procedure's, and the code after the call would run as it did before the selection. It is not refused when
    /// nothing runs under the state it leaves: the code after it cannot raise an error before the procedure ends, and does not go back to the selection.
    /// </summary>
    ErrorHandlingChanged,

    /// <summary>
    /// An <c>On Error GoTo</c> in the selection names a handler outside it, which the new procedure has no label of.
    /// </summary>
    HandlerOutsideTheSelection,

    /// <summary>
    /// A <c>Resume</c> in the selection, about an error raised before it: the new procedure has no error of its own to resume.
    /// </summary>
    ResumeOfAnErrorBefore,

    /// <summary>
    /// The statement before a <c>GoSub</c> routine goes on to it: control that falls into the routine reaches its <c>Return</c> with no <c>GoSub</c>, which
    /// raises run-time error 3, where a call of the procedure it is converted to would not.
    /// </summary>
    FallsIntoTheRoutine,

    /// <summary>
    /// An <c>On…GoSub</c> in a single-line <c>If</c> runs the <c>GoSub</c> routine: the <c>Select Case</c> it becomes, with a call of the procedure the routine is
    /// converted to, cannot be written there.
    /// </summary>
    OnGoSubInASingleLineIf,
}

/// <summary>
/// Where control leaves the selection other than by falling through to the statement after it.
/// </summary>
internal enum ExtractionExitKind
{
    /// <summary>
    /// An <c>Exit Sub</c>, <c>Exit Function</c> or <c>Exit Property</c>: the caller exits too.
    /// </summary>
    Procedure,

    /// <summary>
    /// An <c>Exit For</c> or <c>Exit Do</c> of a loop the selection is inside of.
    /// </summary>
    Loop,

    /// <summary>
    /// A <c>GoTo</c> (or <c>On…GoTo</c>, or a <c>Resume</c> of a label) to a label outside the selection.
    /// </summary>
    Label,
}

/// <summary>
/// A place outside the selection that control leaves it for, which the new procedure tells its caller to go to: one exit code each.
/// </summary>
/// <param name="Kind">What leaves.</param>
/// <param name="Target">The offset of the instruction control goes to; the graph's exit for <see cref="ExtractionExitKind.Procedure"/>.</param>
/// <param name="Label">The label of the target, for <see cref="ExtractionExitKind.Label"/>.</param>
/// <param name="From">The statements in the selection that leave for it.</param>
internal sealed record class ExtractionExit(ExtractionExitKind Kind, int Target, string? Label, ImmutableArray<SourceLocation> From);

/// <summary>
/// What the extracted code would do differently, which the user accepts or cancels: never silent.
/// </summary>
internal enum ExtractionBehaviourChangeKind
{
    /// <summary>
    /// A handler of the source procedure can catch an error of the selection. Extracted, the error is raised to it at the call: a <c>Resume Next</c> of it skips the
    /// rest of the extracted code, and a <c>Resume</c> runs the whole call again.
    /// </summary>
    CallerHandlerResumes,

    /// <summary>
    /// A handler of the source procedure can catch an error of the selection, and none of them resumes: only what <c>Erl</c> says changes. A note.
    /// </summary>
    ErlOnly,

    /// <summary>
    /// The selection runs with <c>On Error Resume Next</c> on some paths and without it on others: the new procedure cannot start as all of them do.
    /// </summary>
    ResumeNextOnSomePaths,

    /// <summary>
    /// The selection has line numbers, and a handler of the source procedure can catch an error of it. <c>Erl</c> then reports the call, as for a statement of
    /// the source procedure's own that raises the error: the call's document line, or with line-label numbering, the call's line number, or on a line without
    /// one, the last line number the source procedure reached before the call.
    /// </summary>
    ErlReportsTheCall,

    /// <summary>
    /// The selection runs under <c>On Error Resume Next</c> and reads <c>Err</c> or <c>Erl</c>, which may be about an error raised before it: the
    /// <c>On Error Resume Next</c> the new procedure starts with clears them. The call does not, and any <c>On Error</c> statement that runs
    /// does (observed in Excel).
    /// </summary>
    ErrClearedOnEntry,

    /// <summary>
    /// A variable that becomes the new procedure's own can hold an object: an object it holds last is released when the new procedure ends, and its
    /// <c>Class_Terminate</c> runs then, before the code the source procedure runs after the call. Left in the source procedure, the variable keeps the
    /// object until that procedure ends.
    /// </summary>
    /// <remarks>
    /// A variable declared as an object, an array of objects or a user-defined type with an object field can hold one; a <c>Variant</c> can when the
    /// selection <c>Set</c>-assigns it, or a <c>For Each</c> in the selection gives it an element.
    /// </remarks>
    ObjectReleasedEarlier,
}

/// <summary>
/// A behaviour change of the extraction, and what it is about.
/// </summary>
/// <param name="Kind">The change.</param>
/// <param name="Handlers">The labels of the handlers it is about.</param>
/// <param name="Resumes">The <c>Resume</c> statements of those handlers.</param>
/// <param name="IsNote">Whether it is a note rather than a change to accept: what only <c>Erl</c> says.</param>
internal sealed record class ExtractionBehaviourChange(ExtractionBehaviourChangeKind Kind, ImmutableArray<string> Handlers, ImmutableArray<SourceLocation> Resumes, bool IsNote)
{
    /// <summary>
    /// The names of the variables it is about, in the order the selection first uses them: those of an
    /// <see cref="ExtractionBehaviourChangeKind.ObjectReleasedEarlier"/>.
    /// </summary>
    public ImmutableArray<string> Variables { get; init; } = [];
}

/// <summary>
/// A reason the selection is not extracted, and where it is.
/// </summary>
/// <param name="Kind">The reason.</param>
/// <param name="Location">What causes it: the expression that writes the element, the expression of the <c>With</c> block.</param>
/// <param name="Name">The name of the variable it is about, when there is one.</param>
internal sealed record class ExtractionRefusal(ExtractionRefusalKind Kind, SourceLocation? Location, string? Name);

/// <summary>
/// The object of the <c>With</c> block the selection is inside of, which the selection uses a member of, relative to the block (<c>.Name</c>): the new procedure is
/// given it, and wraps the extracted code in a <c>With</c> block of its own.
/// </summary>
/// <param name="With">The block.</param>
/// <param name="Variable">The variable the block names.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its declared type; <see langword="null"/> when it is not known.</param>
/// <param name="Passing">How it is passed: by reference for a variable of a <c>Type</c>, whose members the selection may write; by value otherwise, an object.</param>
internal sealed record class ExtractionWithTarget(WithStatementNode With, SemanticId Variable, string Name, VBType? Type, ExtractionPassing Passing);

/// <summary>
/// A variable of the source procedure that the new procedure is given.
/// </summary>
/// <param name="Variable">The variable.</param>
/// <param name="Name">Its name, which the parameter is named for.</param>
/// <param name="Type">Its declared type, which the parameter is declared as; <see langword="null"/> when it is not known (a <c>Variant</c>).</param>
/// <param name="Passing">How it is passed.</param>
/// <param name="Reason">Why.</param>
/// <param name="IsInput">Whether the selection reads a value given before it.</param>
/// <param name="IsOutput">Whether the selection gives it a value that is read after it.</param>
/// <param name="MayBeMissing">
/// Whether it is a parameter of the source procedure that can be missing (an <c>Optional</c> <c>Variant</c> with no default value, or a <c>ParamArray</c>), so that
/// <c>IsMissing</c> of it means something: the new parameter is declared <c>Optional</c> and <c>As Variant</c>, with no default
/// value, and the argument passed on to it is missing there when it is missing in the source procedure.
/// </param>
/// <param name="IsUndeclared">Whether it is declared by being named, with no <c>Option Explicit</c>: the new procedure declares the parameter, of the type its DefType gives it.</param>
internal sealed record class ExtractionParameter(
    SemanticId Variable, string Name, VBType? Type, ExtractionPassing Passing, ExtractionPassingReason Reason, bool IsInput, bool IsOutput, bool MayBeMissing = false,
    bool IsUndeclared = false)
{
    /// <summary>
    /// Whether the new parameter is declared <c>Optional</c>: it <see cref="MayBeMissing"/>, and is no <c>ParamArray</c>. It comes after every parameter that is not.
    /// </summary>
    public bool IsOptional => MayBeMissing && Reason is not ExtractionPassingReason.ParamArray;
}

/// <summary>
/// A variable the new procedure declares as a local of its own.
/// </summary>
/// <param name="Variable">The variable.</param>
/// <param name="Name">Its name.</param>
/// <param name="Type">Its declared type; <see langword="null"/> when it is not known.</param>
/// <param name="MovesDeclaration">
/// Whether its declaration moves into the new procedure: it is used nowhere else. Otherwise the source procedure keeps its own, and the new one declares another:
/// what the selection does with it neither comes from nor goes to the rest of the source procedure.
/// </param>
/// <param name="IsUndeclared">
/// Whether it is declared by being named, with no <c>Option Explicit</c>: the new procedure declares it explicitly, as a <c>Variant</c>, one step towards
/// <c>Option Explicit</c>.
/// </param>
internal sealed record class ExtractionLocal(SemanticId Variable, string Name, VBType? Type, bool MovesDeclaration, bool IsUndeclared = false);

/// <summary>
/// What extracting a selection as a procedure takes: what it is given, what it gives back, and what moves.
/// </summary>
/// <param name="Selection">The statements extracted.</param>
/// <param name="Parameters">The variables the new procedure is given, in the order the selection first touches them.</param>
/// <param name="Locals">The variables the new procedure declares as its own.</param>
/// <param name="MovedOut">
/// The variables declared in the selection whose declaration stays in the source procedure, before the call: those used after the selection, and its <c>Static</c>
/// locals, whose lifetime is the source procedure's.
/// </param>
/// <param name="AssignsResult">Whether the selection assigns the source procedure's own result, which the call site assigns from the new procedure.</param>
/// <param name="ReadsResult">
/// Whether the selection reads a value of the source procedure's own result given outside it: the new procedure is given it, since it cannot name the source's.
/// </param>
/// <param name="WithTarget">The object of the <c>With</c> block the selection is inside of and uses a member of; <see langword="null"/> when there is none.</param>
/// <param name="Refusals">Why the selection is not extracted; empty when it is.</param>
/// <param name="Exits">Where control leaves the selection for, other than the statement after it.</param>
/// <param name="StartsWithResumeNext">
/// Whether the selection runs with <c>On Error Resume Next</c> on every path, which the new procedure starts with: an error resumes inside it, as it does inline.
/// </param>
/// <param name="BehaviourChanges">What the extracted code would do differently, and the notes.</param>
internal sealed record class ExtractionPlan(
    ExtractionSelection Selection,
    ImmutableArray<ExtractionParameter> Parameters,
    ImmutableArray<ExtractionLocal> Locals,
    ImmutableArray<SemanticId> MovedOut,
    bool AssignsResult,
    bool ReadsResult,
    ExtractionWithTarget? WithTarget,
    ImmutableArray<ExtractionRefusal> Refusals,
    ImmutableArray<ExtractionExit> Exits,
    bool StartsWithResumeNext,
    ImmutableArray<ExtractionBehaviourChange> BehaviourChanges)
{
    /// <summary>
    /// Whether the selection is extracted: nothing refuses it.
    /// </summary>
    public bool IsExtractable => Refusals.IsEmpty;

    /// <summary>
    /// The local constants of the source procedure that the selection reads and does not declare: the new procedure declares each as the source does. A constant
    /// is never passed (it is no variable), and its value is the same in both.
    /// </summary>
    public ImmutableArray<SemanticId> Constants { get; init; } = [];

    /// <summary>
    /// Where the selection names the source procedure's own result, read or written (<see cref="AssignsResult"/>, <see cref="ReadsResult"/>): the new procedure
    /// cannot name it, and names what it is given for it there instead.
    /// </summary>
    public ImmutableArray<SourceRange> ResultNames { get; init; } = [];

    /// <summary>
    /// The variables of the source procedure live after the selection, by every way out of it: what the code after the call still reads of what the selection
    /// leaves. The edited source procedure has the same ones live after the call.
    /// </summary>
    public ImmutableHashSet<SemanticId> LiveAfter { get; init; } = [];

    /// <summary>
    /// The parameters the new procedure may return instead: those it only gives a value to, which the call site can assign from it.
    /// </summary>
    public ImmutableArray<ExtractionParameter> ReturnCandidates => [.. Parameters.Where(parameter => parameter.Reason is ExtractionPassingReason.WrittenOnly)];
}
