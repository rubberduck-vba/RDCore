using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Refactoring;
using RDCore.SDK.Server.Configuration;
using RDCore.SDK.Server.ProtocolExtensions;
using RDCore.SDK.Workspace;
using System.Globalization;
using System.Resources;

namespace RDCore.LanguageServer.Refactoring;

/// <summary>
/// Extract Method, as a client asks it of a range of a document (<see cref="RefactoringKinds.ExtractMethod"/>): what it asks and what it refuses or would change,
/// and the edit it makes with the user's answers.
/// </summary>
/// <remarks>
/// It converts a <c>GoSub</c> routine to a procedure as well (<see cref="RefactoringKinds.ConvertGoSub"/>), which extracts the routine a label starts, asked for
/// by kind: the range is on the label, on a <c>GoSub</c> that names it, or on one of its <c>Return</c>s.
/// </remarks>
internal interface IExtractMethodService
{
    /// <summary>
    /// What a refactoring of the kind would ask the user of a range, and what it refuses or would change: Extract Method's, or Convert GoSub's.
    /// </summary>
    /// <param name="kind">The refactoring: <see cref="RefactoringKinds.ExtractMethod"/> or <see cref="RefactoringKinds.ConvertGoSub"/>.</param>
    /// <param name="documentUri">The document.</param>
    /// <param name="range">The range, in the text of the document.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The choices and the notices.</returns>
    Task<RefactorPrepareResult> PrepareAsync(string kind, Uri documentUri, SourceRange range, CancellationToken token);

    /// <summary>
    /// The edit a refactoring of the kind makes of a range with the user's answers, verified.
    /// </summary>
    /// <param name="kind">The refactoring: <see cref="RefactoringKinds.ExtractMethod"/> or <see cref="RefactoringKinds.ConvertGoSub"/>.</param>
    /// <param name="documentUri">The document.</param>
    /// <param name="range">The range, in the text of the document.</param>
    /// <param name="answers">The user's answers; a choice that is not answered takes its default.</param>
    /// <param name="accepted">The codes of the changes the user accepts.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The edit, or why there is none, in the language of the user interface.</returns>
    Task<ExtractMethodAnswer> ApplyAsync(
        string kind, Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token);

    /// <summary>
    /// What the name the user is typing for a refactoring of the kind raises before it is applied.
    /// </summary>
    /// <param name="kind">The refactoring: <see cref="RefactoringKinds.ExtractMethod"/> or <see cref="RefactoringKinds.ConvertGoSub"/>.</param>
    /// <param name="documentUri">The document.</param>
    /// <param name="range">The range, in the text of the document.</param>
    /// <param name="answers">The user's answers, whose name is checked.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The notices, each with the choice it answers.</returns>
    Task<RefactorIdentifierCheckResult> CheckIdentifierAsync(string kind, Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, CancellationToken token);

    /// <summary>
    /// What extracting the statements of a range would ask the user, and what it refuses or would change.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="range">The range, in the text of the document.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The choices and the notices: a range of no procedure of a module of the workspace is refused, as one that holds no statement is.</returns>
    Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourceRange range, CancellationToken token);

    /// <summary>
    /// The edit that extracts the statements of a range with the user's answers, verified: the module it leaves parses, binds every name it did, and does what
    /// it did.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="range">The range, in the text of the document.</param>
    /// <param name="answers">The user's answers; a choice that is not answered takes its default.</param>
    /// <param name="accepted">The codes of the changes the user accepts.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The edit, or why there is none, in the language of the user interface.</returns>
    Task<ExtractMethodAnswer> ApplyAsync(Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token);

    /// <summary>
    /// What the name the user is typing raises before the extraction is applied: one the code cannot declare there, which <see cref="ApplyAsync"/> refuses, or
    /// one that hides a declaration outside the module, which it takes.
    /// </summary>
    /// <param name="documentUri">The document.</param>
    /// <param name="range">The range, in the text of the document.</param>
    /// <param name="answers">The user's answers, whose name is checked; a name that is not answered, or is answered with nothing, raises nothing.</param>
    /// <param name="token">A token that cancels the parses.</param>
    /// <returns>The notices, each with the choice it answers; none for a range that <see cref="PrepareAsync"/> refuses.</returns>
    Task<RefactorIdentifierCheckResult> CheckIdentifierAsync(Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, CancellationToken token);
}

/// <summary>
/// What an extraction is answered with: the edit, or why there is none.
/// </summary>
/// <param name="Edit">The edit.</param>
/// <param name="Refusal">Why there is none, in the language of the user interface.</param>
internal sealed record class ExtractMethodAnswer(RefactorWorkspaceEdit? Edit, string? Refusal);

/// <summary>
/// Extract Method over the workspace as a rename composes it (<see cref="IRenameService.ComposeAsync"/>): the plan of the selection (<see cref="ExtractionAnalysis"/>),
/// the edit it writes (<see cref="ExtractMethodEdit"/>), indented as the user's settings say, and verified against the workspace composed again with the module
/// as the edit leaves it (<see cref="ExtractionVerification"/>).
/// </summary>
/// <remarks>
/// <para>
/// It asks the name of the new procedure, the variable it returns as a <c>Function</c> when the selection gives any back, and whether the exits of a selection
/// that leaves the procedure, or a loop or a block around it, are routed; it shows the parameters and why each is passed as it is, which the user renames,
/// reorders, and passes by reference where they are passed by value (<see cref="ExtractionParameters"/>). Each behavior change of the extraction is a change
/// the user accepts, or a note.
/// </para>
/// <para>
/// A program of the platform's BASIC is not extracted from: its lines are not the module they are the body of.
/// </para>
/// </remarks>
internal sealed class ExtractMethodService(IRenameService rename, IParsingClientService parsing, IOptions<SdkAppOptions> options) : IExtractMethodService
{
    /// <summary>
    /// The id of the choice of the new procedure's name.
    /// </summary>
    public const string NameChoice = "name";

    /// <summary>
    /// The id of the list of the new procedure's parameters.
    /// </summary>
    public const string ParametersChoice = "parameters";

    /// <summary>
    /// The id of the choice of the variable the new procedure returns.
    /// </summary>
    public const string ReturnValueChoice = "returnValue";

    /// <summary>
    /// The id of the choice to route the exits of the selection.
    /// </summary>
    public const string RouteExitsChoice = "routeExits";

    /// <summary>
    /// The id of the choice of Convert GoSub to convert every <c>GoSub</c> routine of the procedure, innermost first, each verified: all of them, or none. It is
    /// offered when the procedure has another routine, and is ticked by default when the routine runs another, which is converted first.
    /// </summary>
    public const string ConvertAllChoice = "convertAll";

    /// <summary>
    /// The id of the choice of a parameter's name, among the choices of an item of <see cref="ParametersChoice"/>.
    /// </summary>
    public const string ParameterNameChoice = "name";

    /// <summary>
    /// The id of the choice of how a parameter is passed, <c>ByVal</c> or <c>ByRef</c>, among the choices of an item of <see cref="ParametersChoice"/>: only a
    /// parameter whose passing is the user's to choose has it (<see cref="ExtractionParameters.PassingIsAChoice"/>).
    /// </summary>
    public const string ParameterPassingChoice = "passing";

    // the value of the option that returns nothing: no variable is named so.
    private const string NoReturnValue = "";

    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.ExtractMethod.ExtractMethodMessages", typeof(ExtractMethodService).Assembly);

    /// <summary>
    /// The title of the code action that extracts a selection, in the language of the user interface.
    /// </summary>
    public static string Title() => Title(RefactoringKinds.ExtractMethod);

    /// <summary>
    /// The title of the code action of a kind of refactoring this service makes, in the language of the user interface.
    /// </summary>
    /// <param name="kind"><see cref="RefactoringKinds.ExtractMethod"/> or <see cref="RefactoringKinds.ConvertGoSub"/>.</param>
    public static string Title(string kind) => Format(kind == RefactoringKinds.ConvertGoSub ? "ConvertGoSub_Title" : "ExtractMethod_Title");

    /// <summary>
    /// Why an extraction offered for one version of a document is not made for another, in the language of the user interface.
    /// </summary>
    public static string DocumentChanged() => DocumentChanged(RefactoringKinds.ExtractMethod);

    /// <summary>
    /// Why a refactoring of a kind this service makes, offered for one version of a document, is not made for another, in the language of the user interface.
    /// </summary>
    /// <param name="kind"><see cref="RefactoringKinds.ExtractMethod"/> or <see cref="RefactoringKinds.ConvertGoSub"/>.</param>
    public static string DocumentChanged(string kind) => Format(kind == RefactoringKinds.ConvertGoSub ? "ConvertGoSub_DocumentChanged" : "ExtractMethod_DocumentChanged");

    public Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourceRange range, CancellationToken token)
        => PrepareAsync(RefactoringKinds.ExtractMethod, documentUri, range, token);

    public Task<ExtractMethodAnswer> ApplyAsync(
        Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token)
        => ApplyAsync(RefactoringKinds.ExtractMethod, documentUri, range, answers, accepted, token);

    public Task<RefactorIdentifierCheckResult> CheckIdentifierAsync(Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, CancellationToken token)
        => CheckIdentifierAsync(RefactoringKinds.ExtractMethod, documentUri, range, answers, token);

    public async Task<RefactorPrepareResult> PrepareAsync(string kind, Uri documentUri, SourceRange range, CancellationToken token)
    {
        if (await ExtractionOfAsync(kind, documentUri, range, token) is not { } extraction)
        {
            var refused = nameof(ExtractionSelectionRefusal.NoStatement);
            return new RefactorPrepareResult
            {
                Notices = [new RefactorNotice { Code = Code(refused), Kind = RefactorNoticeKind.Refusal, Message = Format($"ExtractMethodSelection_{refused}") }],
            };
        }

        if (extraction.Plan is not { } plan)
        {
            return new RefactorPrepareResult { Notices = [extraction.SelectionRefusal!] };
        }

        // from here the selection snapped: the statements it is of are what the editor shows will move, refused or not.
        var notices = extraction.Notices();
        var moved = plan.Selection.Range;

        // a routine that runs others converts with them, which are converted first: what refuses it alone is a note.
        if (RunsOtherRoutines(plan))
        {
            return new RefactorPrepareResult
            {
                Choices = extraction.Choices(),
                Notices =
                [
                    .. plan.Refusals.Select(refusal => extraction.NoteOf(refusal, "ConvertGoSub_RunsAnother")),
                    .. notices.Where(notice => notice.Kind is not RefactorNoticeKind.Refusal),
                ],
                Range = moved,
            };
        }

        if (!plan.IsExtractable)
        {
            return new RefactorPrepareResult { Notices = notices, Range = moved };
        }

        // a refusal of the writer that no answer can lift is said before the user answers anything.
        var written = ExtractMethodEdit.Of(plan, extraction.Source, new ExtractionChoices(extraction.SuggestedName()));
        if (written.Refusal is ExtractionEditRefusalKind.ExitByJumpTable or ExtractionEditRefusalKind.RoutineUsesTheResult or ExtractionEditRefusalKind.RoutineRunsItself
            or ExtractionEditRefusalKind.RoutineInAWithBlock)
        {
            return new RefactorPrepareResult
            {
                Notices = [new RefactorNotice { Code = Code(written.Refusal.Value.ToString()), Kind = RefactorNoticeKind.Refusal, Message = EditRefusal(written.Refusal.Value, extraction) }, .. notices],
                Range = moved,
            };
        }

        return new RefactorPrepareResult { Choices = extraction.Choices(), Notices = notices, Range = moved };
    }

    public async Task<RefactorIdentifierCheckResult> CheckIdentifierAsync(
        string kind, Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, CancellationToken token)
    {
        // the name as ApplyAsync reads it: trimmed, and one that is not answered takes its default, which raises nothing.
        if (answers.FirstOrDefault(answer => answer.Id == NameChoice)?.Value is not { Length: > 0 } typed
            || await ExtractionOfAsync(kind, documentUri, range, token) is not { Plan: { IsExtractable: true } plan } extraction)
        {
            return new RefactorIdentifierCheckResult();
        }

        return new RefactorIdentifierCheckResult
        {
            Notices = [.. ExtractionName.Validate(typed.Trim(), plan, extraction.Source, extraction.Part.Context, extraction.Module.Module).Select(problem => new RefactorNotice
            {
                Code = Code($"Name{problem.Kind}"),
                Kind = problem.IsWarning ? RefactorNoticeKind.Note : RefactorNoticeKind.Refusal,
                Message = problem.ToMessage(RenameWorkspace.Locator(extraction.Part)),
                Related = extraction.DeclarationOf(problem),
                Choice = NameChoice,
            })],
        };
    }

    public async Task<ExtractMethodAnswer> ApplyAsync(
        string kind, Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token)
    {
        if (await ExtractionOfAsync(kind, documentUri, range, token) is not { } extraction)
        {
            return new(null, Format("ExtractMethodSelection_NoStatement"));
        }

        if (extraction.Plan is not { } plan)
        {
            return new(null, extraction.SelectionRefusal!.Message);
        }

        // a routine converts alone, or with every other of the procedure: by default when it runs another, which is converted first.
        if (kind == RefactoringKinds.ConvertGoSub
            && (bool.TryParse(answers.FirstOrDefault(answer => answer.Id == ConvertAllChoice)?.Value, out var all) ? all : RunsOtherRoutines(plan)))
        {
            return await ConvertAllAsync(extraction, answers, accepted, token);
        }

        if (!plan.IsExtractable)
        {
            return new(null, extraction.RefusalMessage(plan.Refusals[0]));
        }

        var (choices, refused) = ChoicesOf(extraction, answers, accepted);
        if (choices is null)
        {
            return new(null, refused);
        }

        var (formatted, failure) = await WrittenAsync(extraction, choices, token);
        if (formatted is null)
        {
            return new(null, failure);
        }

        var document = extraction.Module.Document;
        return new(new RefactorWorkspaceEdit
        {
            Documents =
            [
                new RefactorDocumentEdit
                {
                    Uri = document.Id.Uri.ToUri(),
                    Version = document.Version,
                    Edits = [.. formatted.Edits.Select(each => new RefactorTextEdit { Range = each.Range, NewText = each.NewText })],
                },
            ],
        }, null);
    }

    // what the user chose, from the answers, each to its choice; one that is not given takes the choice's default. Null, with why, when an answer is refused.
    private static (ExtractionChoices? Choices, string? Refusal) ChoicesOf(Extraction extraction, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted)
    {
        var plan = extraction.Plan!;
        string? Answer(string id) => answers.FirstOrDefault(answer => answer.Id == id)?.Value;
        var name = Answer(NameChoice) is { Length: > 0 } typed ? typed.Trim() : extraction.SuggestedName();
        var returned = Answer(ReturnValueChoice) ?? NoReturnValue;
        var returnValue = returned.Length == 0 ? null : plan.ReturnCandidates.FirstOrDefault(candidate => candidate.Name.Equals(returned, StringComparison.OrdinalIgnoreCase));
        if (returned.Length > 0 && returnValue is null)
        {
            return (null, Format($"ExtractMethodEdit_{ExtractionEditRefusalKind.NotAReturnCandidate}", returned));
        }

        var routeExits = !bool.TryParse(Answer(RouteExitsChoice), out var route) || route;

        // a name that cannot be declared there is refused; one that hides another declaration is the user's to choose.
        if (ExtractionName.Validate(name, plan, extraction.Source, extraction.Part.Context, extraction.Module.Module).FirstOrDefault(problem => !problem.IsWarning) is { } problem)
        {
            return (null, problem.ToMessage(RenameWorkspace.Locator(extraction.Part)));
        }

        // a change of what the code does is made only when the user accepts it.
        if (plan.BehaviourChanges.Where(change => !change.IsNote).FirstOrDefault(change => !accepted.Contains(Code(change.Kind.ToString()))) is { } declined)
        {
            return (null, Format("ExtractMethodEdit_NotAccepted", extraction.ChangeMessage(declined)));
        }

        // the parameters as the user renamed them, put them in order and passes them; an item that is not answered is kept as it is, where it is.
        var parameterAnswers = answers.FirstOrDefault(answer => answer.Id == ParametersChoice)?.Items
            .Select(item => new ExtractionParameterAnswer(item.Id,
                item.Answers.FirstOrDefault(each => each.Id == ParameterNameChoice)?.Value,
                item.Answers.FirstOrDefault(each => each.Id == ParameterPassingChoice)?.Value))
            .ToArray() ?? [];
        var (parameters, parameterProblem) = ExtractionParameters.Choose(parameterAnswers, plan, extraction.Source, name, extraction.Part.Context, extraction.Module.Module);
        return parameterProblem is not null
            ? (null, parameterProblem.ToMessage())
            : (new ExtractionChoices(name) { ReturnValue = returnValue?.Variable, RouteExits = routeExits, Parameters = parameters }, null);
    }

    // the extraction written with the choices, indented as the user's settings say, and verified; null, with why, when it is refused.
    private async Task<(ExtractionEdit? Formatted, string? Refusal)> WrittenAsync(Extraction extraction, ExtractionChoices choices, CancellationToken token)
    {
        var edit = ExtractMethodEdit.Of(extraction.Plan!, extraction.Source, choices);
        if (edit.Refusal is { } refusal)
        {
            return (null, EditRefusal(refusal, extraction));
        }

        var text = extraction.Analysis.Text;
        var documentAddress = extraction.Module.Document.Id.Uri.ToUri();
        var formatted = edit.Formatted(text, await ParseAsync(documentAddress, edit.ApplyTo(text), extraction, token), options.Value.Formatting.ToOptions());
        return await VerifiedAsync(extraction, formatted, choices.Name, documentAddress, token) is { } failure
            ? (null, Format("ExtractMethodVerification_Failed", Format($"ExtractMethodVerification_{failure.Kind}", failure.Detail)))
            : (formatted, null);
    }

    // every GoSub routine of the procedure converted, innermost first, each on the text the one before it left, and verified there: all of them, or none. The
    // routine asked for takes the user's answers; each other one, its default name and parameters, and the changes the user accepted.
    private async Task<ExtractMethodAnswer> ConvertAllAsync(Extraction asked, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token)
    {
        var askedLabel = asked.Plan!.Selection.Routine!.Label;
        var (member, memberKind) = (asked.Procedure.Declaration.Name, asked.Procedure.Declaration.MemberKind);
        var remaining = RoutineLabels(asked.Procedure).ToList();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = asked;
        while (remaining.Count > 0)
        {
            // the next routine is the first that converts as the text now is: one that runs another that is still a routine waits for it.
            Extraction? next = null;
            string? why = null;
            foreach (var each in remaining)
            {
                var (candidate, refusal) = RoutineOf(current, each);
                if (candidate is not null)
                {
                    next = candidate;
                    break;
                }

                why ??= refusal;
            }

            if (next is null)
            {
                return new(null, Format("ConvertGoSub_NotAll", remaining[0], why));
            }

            var label = next.Plan!.Selection.Routine!.Label;
            var isAsked = label.Equals(askedLabel, StringComparison.OrdinalIgnoreCase);
            var (choices, refused) = ChoicesOf(next, isAsked ? answers : [], accepted);
            if (choices is null)
            {
                return new(null, refused);
            }

            // the names the run gave already are taken: the next suggestion has a number after it.
            var name = choices.Name;
            for (var index = 2; !isAsked && named.Contains(name); index++)
            {
                name = $"{choices.Name}{index}";
            }

            if (named.Contains(name))
            {
                return new(null, Format("ConvertGoSub_NameTaken", name));
            }

            var (formatted, failure) = await WrittenAsync(next, choices with { Name = name }, token);
            if (formatted is null)
            {
                return new(null, failure);
            }

            named.Add(name);
            remaining.RemoveAll(converted => converted.Equals(label, StringComparison.OrdinalIgnoreCase));
            if (await ReanalyzedAsync(next, formatted.ApplyTo(next.Analysis.Text), member, memberKind, token) is not { } reanalyzed)
            {
                return new(null, Format("ExtractMethodVerification_Failed", Format($"ExtractMethodVerification_{ExtractionVerificationFailureKind.DoesNotParse}", string.Empty)));
            }

            current = reanalyzed;
        }

        var document = asked.Module.Document;
        return new(new RefactorWorkspaceEdit
        {
            Documents =
            [
                new RefactorDocumentEdit
                {
                    Uri = document.Id.Uri.ToUri(),
                    Version = document.Version,
                    Edits = [Difference(asked.Analysis.Text, current.Analysis.Text)],
                },
            ],
        }, null);
    }

    // the routine a label starts, as the text of the extraction is, when it converts as it is; null, with why, when it does not.
    private static (Extraction? Routine, string? Refusal) RoutineOf(Extraction extraction, string label)
    {
        var procedure = extraction.Procedure;
        if (!GoSubRoutine.TryFind(procedure.Declaration, procedure.Instructions, procedure.Graph, extraction.Analysis.Directives, label, out var routine, out var why))
        {
            return (null, Format($"ConvertGoSub_{why}", label));
        }

        var plan = ExtractionAnalysis.Plan(routine!, procedure.Graph, procedure.Accesses, procedure.Model, extraction.Analysis.Scopes);
        var found = extraction with { Plan = plan };
        if (!plan.IsExtractable)
        {
            return (null, found.RefusalMessage(plan.Refusals[0]));
        }

        return ExtractMethodEdit.Of(plan, found.Source, new ExtractionChoices(found.SuggestedName())).Refusal is { } refusal
            and not ExtractionEditRefusalKind.NotExtractable
            ? (null, EditRefusal(refusal, found))
            : (found, null);
    }

    // the module analyzed again as the text is: the procedure, and its selection to come.
    private async Task<Extraction?> ReanalyzedAsync(Extraction extraction, string text, string member, MemberKind memberKind, CancellationToken token)
    {
        var parse = await ParseAsync(extraction.Module.Document.Id.Uri.ToUri(), text, extraction, token);
        if (extraction.Part.Recompose is not { } recompose || parse.SyntaxTree is not { } tree)
        {
            return null;
        }

        var composition = recompose(extraction.Module.Module, parse);
        var analysis = ExtractionModuleAnalysis.Of(extraction.Module.Module, text, parse, composition.Resolver, composition.ScopeTree);
        if (analysis.Procedure(member, memberKind) is not { } procedure)
        {
            return null;
        }

        return extraction with
        {
            Analysis = analysis,
            Procedure = procedure,
            Source = new ExtractionSource(tree, procedure.Declaration, procedure.Model.Procedure, text, analysis.Scopes),
            Plan = null,
        };
    }

    // the one edit that makes the text the other: what lies between what they start and end with alike.
    private static RefactorTextEdit Difference(string before, string after)
    {
        var start = 0;
        while (start < before.Length && start < after.Length && before[start] == after[start])
        {
            start++;
        }

        var end = 0;
        while (end < before.Length - start && end < after.Length - start && before[^(end + 1)] == after[^(end + 1)])
        {
            end++;
        }

        var text = new ExtractionText(before);
        return new RefactorTextEdit
        {
            Range = new SourceRange(text.PositionOf(start), text.PositionOf(before.Length - end)),
            NewText = after[start..(after.Length - end)],
        };
    }

    // the extraction verified against the workspace composed again with the module as the edit leaves it.
    private async Task<ExtractionVerificationFailure?> VerifiedAsync(Extraction extraction, ExtractionEdit edit, string name, Uri documentAddress, CancellationToken token)
    {
        var edited = edit.ApplyTo(extraction.Analysis.Text);
        var parse = await ParseAsync(documentAddress, edited, extraction, token);
        if (extraction.Part.Recompose is not { } recompose)
        {
            return new(ExtractionVerificationFailureKind.DoesNotParse, null);
        }

        var composition = recompose(extraction.Module.Module, parse);
        var after = ExtractionModuleAnalysis.Of(extraction.Module.Module, edited, parse, composition.Resolver, composition.ScopeTree);

        // the verification analyzes the text the edit leaves, which is the one parsed here.
        var source = extraction.Procedure.Declaration;
        return ExtractionVerification.Verify(extraction.Plan!, edit, name, source.Name, source.MemberKind, extraction.Analysis, _ => after);
    }

    // a text of the module, parsed as the document is: the designer of a form is its document's, which the text of its code does not hold.
    private async Task<SDK.Model.AST.ModuleParseResult> ParseAsync(Uri documentAddress, string text, Extraction extraction, CancellationToken token)
        => await parsing.ParseFragmentAsync(documentAddress, text, token) with { Designer = extraction.Module.Parse.Designer };

    // the module, the procedure and the selection a range is of, or the GoSub routine for Convert GoSub; null when it is of no procedure of a module of the
    // workspace.
    private async Task<Extraction?> ExtractionOfAsync(string kind, Uri documentUri, SourceRange range, CancellationToken token)
    {
        if (await rename.ComposeAsync(token) is not { } workspace
            || workspace.DocumentOf(documentUri) is not ({ } part, { } module)
            || BasicProgramText.IsProgram(module.Document.Id.Uri.GetFileSystemPath())
            || module.Parse.SyntaxTree is not { } tree
            || tree.Children.OfType<MemberDeclarationNode>().FirstOrDefault(member => Contains(member.SourceLocation.Range, range.Start)) is not { } member)
        {
            return null;
        }

        var analysis = ExtractionModuleAnalysis.Of(module.Module, module.Source, module.Parse, part.Context.Resolver, part.Context.ScopeTree);
        if (analysis.Procedure(member.Name, member.MemberKind) is not { } procedure)
        {
            return null;
        }

        var source = new ExtractionSource(tree, procedure.Declaration, procedure.Model.Procedure, module.Source, analysis.Scopes);
        if (kind == RefactoringKinds.ConvertGoSub)
        {
            var label = LabelAt(procedure.Declaration, range.Start, new ExtractionText(module.Source))
                ?? ReturningLabelAt(procedure.Declaration, range.Start, candidate => GoSubRoutine.TryFind(
                    procedure.Declaration, procedure.Instructions, procedure.Graph, analysis.Directives, candidate, out var found, out _) ? found!.Routine : null);
            ExtractionSelection? routine = null;
            var why = GoSubRoutineRefusal.NoLabel;
            if (label is null || !GoSubRoutine.TryFind(procedure.Declaration, procedure.Instructions, procedure.Graph, analysis.Directives, label, out routine, out why))
            {
                return new Extraction(part, module, analysis, procedure, source, null,
                    new RefactorNotice { Code = Code(why.ToString()), Kind = RefactorNoticeKind.Refusal, Message = Format($"ConvertGoSub_{why}", label) });
            }

            return new Extraction(part, module, analysis, procedure, source, ExtractionAnalysis.Plan(routine!, procedure.Graph, procedure.Accesses, procedure.Model, analysis.Scopes), null);
        }

        if (!ExtractionSelection.TrySnap(procedure.Declaration, procedure.Instructions, analysis.Directives, range, out var selection, out var refused, out var cut))
        {
            var where = cut is null ? string.Empty : RenameWorkspace.Locator(part)(module.Module, cut.SourceLocation.Range.Start);
            return new Extraction(part, module, analysis, procedure, source, null, new RefactorNotice
            {
                Code = Code(refused.ToString()),
                Kind = RefactorNoticeKind.Refusal,
                Message = Format($"ExtractMethodSelection_{refused}", null, where),
                Location = cut is null ? null : new SourceLocation(module.Document.Id.Uri.ToUri(), cut.SourceLocation.Range),
            });
        }

        return new Extraction(part, module, analysis, procedure, source, ExtractionAnalysis.Plan(selection!, procedure.Graph, procedure.Accesses, procedure.Model, analysis.Scopes), null);
    }

    private static bool Contains(SourceRange range, SourcePosition position) => range.Start <= position && position <= range.End;

    // the label of the GoSub routine a position is on: a label on the position's line, or the one a GoSub there names; null when it is on neither.
    private static string? LabelAt(MemberDeclarationNode procedure, SourcePosition position, ExtractionText text)
    {
        var nodes = procedure.Descendants().ToArray();
        if (nodes.OfType<LineLabelNode>().FirstOrDefault(label => label.SourceLocation.Range.Start.Line == position.Line) is { } written)
        {
            return written.Name;
        }

        return nodes.OfType<GoSubStatementNode>().FirstOrDefault(statement => Contains(statement.SourceLocation.Range, position)) is { } named
            ? text.Of(named.LabelExpression.SourceLocation.Range).Trim()
            : null;
    }

    // the labels of the procedure that a GoSub or an On…GoSub goes to, as they are written, in the order they are.
    private static IReadOnlyList<string> RoutineLabels(ExtractionProcedureAnalysis procedure)
    {
        var items = procedure.Instructions.Items;
        return [.. procedure.Declaration.Descendants().OfType<LineLabelNode>()
            .Where(label => procedure.Instructions.TryGetLabelOffset(label.Name, out var start)
                && items.Any(item => item.Kind is InstructionKind.GoSub && item.Target == start || item.Kind is InstructionKind.GoSubTable && item.Targets.Contains(start)))
            .Select(label => label.Name)];
    }

    // whether a routine is refused only for the GoSubs in it to other routines, which convert with it, first.
    private static bool RunsOtherRoutines(ExtractionPlan plan)
        => plan.Selection.Routine is not null && plan.Refusals.Length > 0 && plan.Refusals.All(refusal => refusal.Kind is ExtractionRefusalKind.GoSubToOutside);

    // the label of the GoSub routine a Return at a position returns from: the first label of the procedure whose routine has that Return; null when the
    // position is on no Return, or the Return is of no routine that can be found.
    private static string? ReturningLabelAt(MemberDeclarationNode procedure, SourcePosition position, Func<string, GoSubRoutine?> routineOf)
    {
        var nodes = procedure.Descendants().ToArray();
        if (nodes.OfType<ReturnStatementNode>().FirstOrDefault(statement => Contains(statement.SourceLocation.Range, position)) is not { } returned)
        {
            return null;
        }

        return nodes.OfType<LineLabelNode>()
            .Select(label => routineOf(label.Name))
            .FirstOrDefault(routine => routine is not null && routine.Returns.Any(each => each.SourceLocation.Range == returned.SourceLocation.Range))?.Label;
    }

    /// <summary>
    /// The code of the notice that refuses an extraction for <paramref name="refusal"/>.
    /// </summary>
    /// <param name="refusal">Why the extraction is refused.</param>
    public static string RefusalCode(ExtractionRefusalKind refusal) => Code(refusal.ToString());

    // the code of a notice: the same in any language, by which the user accepts a change.
    private static string Code(string kind) => $"extractMethod.{char.ToLowerInvariant(kind[0])}{kind[1..]}";

    private static string EditRefusal(ExtractionEditRefusalKind refusal, Extraction extraction) => refusal is ExtractionEditRefusalKind.NotExtractable
        ? extraction.RefusalMessage(extraction.Plan!.Refusals[0])
        : Format($"ExtractMethodEdit_{refusal}");

    private static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentUICulture, Messages.GetString(key, CultureInfo.CurrentUICulture) ?? key, arguments);

    // a selection of a procedure of a module, and its plan; null with why the selection is refused.
    private sealed record class Extraction(
        RenameWorkspace.ProjectPart Part, RenameWorkspace.DocumentModule Module, ExtractionModuleAnalysis Analysis, ExtractionProcedureAnalysis Procedure,
        ExtractionSource Source, ExtractionPlan? Plan, RefactorNotice? SelectionRefusal)
    {
        private Uri DocumentAddress => Module.Document.Id.Uri.ToUri();

        public string SuggestedName() => ExtractionName.Suggest(Plan!, Source, Part.Context, Module.Module);

        public string Where(SourceLocation? location) => location is { } at ? RenameWorkspace.Locator(Part)(Module.Module, at.Range.Start) : string.Empty;

        public string RefusalMessage(ExtractionRefusal refusal) => RefusalMessage(refusal, $"ExtractMethodRefusal_{refusal.Kind}");

        // a refusal said by another message, which takes the same name and place.
        public string RefusalMessage(ExtractionRefusal refusal, string key) => Format(key, refusal.Name, Where(refusal.Location));

        // a refusal that another choice lifts, said as a note.
        public RefactorNotice NoteOf(ExtractionRefusal refusal, string key) => new()
        {
            Code = Code(refusal.Kind.ToString()),
            Kind = RefactorNoticeKind.Note,
            Message = RefusalMessage(refusal, key),
            Location = refusal.Location is { } at ? new SourceLocation(DocumentAddress, at.Range) : null,
        };

        // the name a problem of a name points to, in the document of a module of the workspace; null for a declaration of a library, or of no module.
        public SourceLocation? DeclarationOf(ExtractionNameProblem problem)
        {
            if (problem.Module is not { } module || Part.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is not { } document)
            {
                return null;
            }

            var end = new SourcePosition(problem.Position.Line, problem.Position.Character + problem.Name.Length);
            var text = document.ToTextRange(new SourceRange(problem.Position, end));
            return new SourceLocation(document.Document.Id.Uri.ToUri(), new SourceRange(text.Start.Line, text.Start.Character, text.End.Line, text.End.Character));
        }

        public string ChangeMessage(ExtractionBehaviourChange change)
            => Format($"ExtractMethodChange_{change.Kind}", string.Join(", ", change.Handlers), string.Join(", ", change.Variables));

        // why the plan refuses the selection, then what the extraction would change and the notes.
        public IReadOnlyList<RefactorNotice> Notices() =>
        [
            .. Plan!.Refusals.Select(refusal => new RefactorNotice
            {
                Code = Code(refusal.Kind.ToString()),
                Kind = RefactorNoticeKind.Refusal,
                Message = RefusalMessage(refusal),
                Location = refusal.Location is { } at ? new SourceLocation(DocumentAddress, at.Range) : null,
            }),
            .. Plan.BehaviourChanges.Select(change => new RefactorNotice
            {
                Code = Code(change.Kind.ToString()),
                Kind = change.IsNote ? RefactorNoticeKind.Note : RefactorNoticeKind.Change,
                Message = ChangeMessage(change),
                Location = change.Resumes is [var first, ..] ? new SourceLocation(DocumentAddress, first.Range) : null,
            }),
        ];

        public IReadOnlyList<RefactorChoice> Choices()
        {
            var plan = Plan!;
            List<RefactorChoice> choices =
            [
                new()
                {
                    Id = NameChoice,
                    Kind = RefactorChoiceKind.Name,
                    Label = Format("ExtractMethodChoice_Name"),
                    Description = Format("ExtractMethodChoice_NameDescription"),
                    Default = SuggestedName(),
                },
            ];

            // the parameters are shown, with why each is passed as it is: the user renames them, puts them in another order, and passes by reference one the
            // plan passes by value.
            if (!plan.Parameters.IsEmpty)
            {
                choices.Add(new()
                {
                    Id = ParametersChoice,
                    Kind = RefactorChoiceKind.List,
                    Label = Format("ExtractMethodChoice_Parameters"),
                    Description = Format("ExtractMethodChoice_ParametersDescription"),
                    CanReorder = true,
                    Items = [.. ExtractionParameters.InDeclarationOrder(plan).Select(parameter => new RefactorItem
                    {
                        Id = parameter.Name,
                        Label = ExtractMethodEdit.ParameterTextOf(plan, Source, parameter),
                        Reason = Format($"ExtractMethodReason_{parameter.Reason}"),
                        Choices = ParameterChoices(parameter),
                    })],
                });
            }

            // a routine's calls are statements, which take no value back.
            if (plan.ReturnCandidates is { IsEmpty: false } candidates && plan.Selection.Routine is null)
            {
                choices.Add(new()
                {
                    Id = ReturnValueChoice,
                    Kind = RefactorChoiceKind.Option,
                    Label = Format("ExtractMethodChoice_ReturnValue"),
                    Description = Format("ExtractMethodChoice_ReturnValueDescription"),
                    Default = NoReturnValue,
                    Options = [new() { Value = NoReturnValue, Label = Format("ExtractMethodChoice_ReturnValueNone") },
                        .. candidates.Select(candidate => new RefactorOption { Value = candidate.Name, Label = candidate.Name })],
                });
            }

            if (!plan.Exits.IsEmpty)
            {
                choices.Add(new()
                {
                    Id = RouteExitsChoice,
                    Kind = RefactorChoiceKind.Flag,
                    Label = Format("ExtractMethodChoice_RouteExits"),
                    Description = Format("ExtractMethodChoice_RouteExitsDescription"),
                    Default = "true",
                });
            }

            if (plan.Selection.Routine is not null && RoutineLabels(Procedure).Count > 1)
            {
                choices.Add(new()
                {
                    Id = ConvertAllChoice,
                    Kind = RefactorChoiceKind.Flag,
                    Label = Format("ConvertGoSub_ConvertAll"),
                    Description = Format("ConvertGoSub_ConvertAllDescription"),
                    Default = RunsOtherRoutines(plan) ? "true" : "false",
                });
            }

            return choices;
        }

        // the name of a parameter, and how it is passed when that is the user's to choose.
        private static IReadOnlyList<RefactorChoice> ParameterChoices(ExtractionParameter parameter)
        {
            List<RefactorChoice> choices =
            [
                new()
                {
                    Id = ParameterNameChoice,
                    Kind = RefactorChoiceKind.Name,
                    Label = Format("ExtractMethodChoice_ParameterName"),
                    Default = parameter.Name,
                },
            ];

            if (ExtractionParameters.PassingIsAChoice(parameter))
            {
                choices.Add(new()
                {
                    Id = ParameterPassingChoice,
                    Kind = RefactorChoiceKind.Option,
                    Label = Format("ExtractMethodChoice_ParameterPassing"),
                    Default = ExtractionParameters.ValueOf(parameter.Passing),
                    Options =
                    [
                        new() { Value = ExtractionParameters.ValueOf(ExtractionPassing.ByVal), Label = "ByVal", Description = Format("ExtractMethodChoice_ParameterByVal") },
                        new() { Value = ExtractionParameters.ValueOf(ExtractionPassing.ByRef), Label = "ByRef", Description = Format("ExtractMethodChoice_ParameterByRef") },
                    ],
                });
            }

            return choices;
        }
    }
}
