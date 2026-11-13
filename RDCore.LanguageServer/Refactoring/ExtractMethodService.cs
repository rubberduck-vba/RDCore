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
using System.Collections.Immutable;
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
internal sealed record class ExtractMethodAnswer(RefactorWorkspaceEdit? Edit, string? Refusal)
{
    /// <summary>
    /// The duplicates of the selection the edit replaces with a call, each with the edits of its document that replace it: a client with nobody to ask
    /// confirms each.
    /// </summary>
    public IReadOnlyList<ExtractMethodReplacement> Replacements { get; init; } = [];
}

/// <summary>
/// A duplicate of the selection that an extraction replaces with a call to the new procedure.
/// </summary>
/// <param name="Id">The duplicate, as the item of the list of duplicates names it.</param>
/// <param name="Label">Where it is and what it starts with, in the language of the user interface.</param>
/// <param name="Document">The document it is in.</param>
/// <param name="Ranges">The ranges of the edits of <paramref name="Document"/> that replace it.</param>
internal sealed record class ExtractMethodReplacement(string Id, string Label, Uri Document, IReadOnlyList<SourceRange> Ranges);

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

    /// <summary>
    /// The id of the choice of how far the new procedure is seen: <c>Private</c>, the default, or <c>Public</c>.
    /// </summary>
    public const string AccessibilityChoice = "accessibility";

    /// <summary>
    /// The id of the choice to replace the duplicates of the selection with calls to the new procedure, those of <see cref="DuplicatesChoice"/> that are
    /// ticked; <c>true</c> by default.
    /// </summary>
    public const string ReplaceDuplicatesChoice = "replaceDuplicates";

    /// <summary>
    /// The id of the list of the duplicates of the selection (<see cref="ExtractionDuplicates"/>), each with its <see cref="DuplicateReplaceChoice"/>.
    /// </summary>
    public const string DuplicatesChoice = "duplicates";

    /// <summary>
    /// The id of the choice to replace a duplicate, among the choices of an item of <see cref="DuplicatesChoice"/>: ticked by default for one in the new
    /// procedure's standard module, which a <c>Private</c> procedure replaces; unticked for one in another module, or in its class module, which only a
    /// <c>Public</c> procedure replaces.
    /// </summary>
    public const string DuplicateReplaceChoice = "replace";

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
        if (written.Refusal is ExtractionEditRefusalKind.RoutineUsesTheResult or ExtractionEditRefusalKind.RoutineRunsItself or ExtractionEditRefusalKind.RoutineInAWithBlock)
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
        // the name is checked as declared with the accessibility answered, and as Private when that is none.
        if (answers.FirstOrDefault(answer => answer.Id == NameChoice)?.Value is not { Length: > 0 } typed
            || await ExtractionOfAsync(kind, documentUri, range, token) is not { Plan: { IsExtractable: true } plan } extraction)
        {
            return new RefactorIdentifierCheckResult();
        }

        var accessibility = AccessibilityOf(answers) ?? ExtractionAccessibility.Private;
        return new RefactorIdentifierCheckResult
        {
            Notices = [.. ExtractionName.Validate(typed.Trim(), plan, extraction.Source, extraction.Part.Context, extraction.Module.Module, accessibility).Select(problem => new RefactorNotice
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

        var (replaced, duplicateRefusal) = extraction.ChosenDuplicates(answers, choices);
        if (duplicateRefusal is not null)
        {
            return new(null, duplicateRefusal);
        }

        var (written, replacements, failure) = await WrittenAsync(extraction, choices, replaced, token);
        if (written is null)
        {
            return new(null, failure);
        }

        return new(new RefactorWorkspaceEdit
        {
            Documents =
            [
                .. written.Values.Select(module => new RefactorDocumentEdit
                {
                    Uri = module.Module.Document.Id.Uri.ToUri(),
                    Version = module.Module.Document.Version,
                    Edits = [.. module.Edit.Edits.Select(each => new RefactorTextEdit { Range = each.Range, NewText = each.NewText })],
                }),
            ],
        }, null)
        {
            Replacements =
            [
                .. replacements.Select(each => new ExtractMethodReplacement(
                    each.Duplicate.Id, Format("ExtractMethodDuplicate_Replacement", extraction.Where(each.Duplicate)), DocumentOf(extraction, each.Duplicate.Module),
                    [.. ReplacementRanges(written[each.Duplicate.Module.AbsoluteUri].Edit, each.Edits)])),
            ],
        };
    }

    // what the user chose, from the answers, each to its choice; one that is not given takes the choice's default. Null, with why, when an answer is refused.
    private static (ExtractionChoices? Choices, string? Refusal) ChoicesOf(Extraction extraction, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted)
    {
        var plan = extraction.Plan!;
        string? Answer(string id) => answers.FirstOrDefault(answer => answer.Id == id)?.Value;
        if (AccessibilityOf(answers) is not { } accessibility)
        {
            return new(null, Format("ExtractMethodChoice_AccessibilityUnknown", Answer(AccessibilityChoice)));
        }

        var name = Answer(NameChoice) is { Length: > 0 } typed ? typed.Trim() : extraction.SuggestedName(accessibility);
        var returned = Answer(ReturnValueChoice) ?? NoReturnValue;
        var returnValue = returned.Length == 0 ? null : plan.ReturnCandidates.FirstOrDefault(candidate => candidate.Name.Equals(returned, StringComparison.OrdinalIgnoreCase));
        if (returned.Length > 0 && returnValue is null)
        {
            return (null, Format($"ExtractMethodEdit_{ExtractionEditRefusalKind.NotAReturnCandidate}", returned));
        }

        var routeExits = !bool.TryParse(Answer(RouteExitsChoice), out var route) || route;

        // a name that cannot be declared there is refused; one that hides another declaration is the user's to choose.
        if (ExtractionName.Validate(name, plan, extraction.Source, extraction.Part.Context, extraction.Module.Module, accessibility)
            .FirstOrDefault(problem => !problem.IsWarning) is { } problem)
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
            : (new ExtractionChoices(name) { ReturnValue = returnValue?.Variable, RouteExits = routeExits, Parameters = parameters, Accessibility = accessibility }, null);
    }

    // the extraction written with the choices and the duplicates replaced: the edits of each module, the selection's and those that replace each duplicate in it,
    // indented as the user's settings say, and verified. Null, with why, when they are refused.
    private async Task<(Dictionary<string, WrittenModule>? Written, List<(ExtractionDuplicate Duplicate, ImmutableArray<ExtractionTextEdit> Edits)> Replacements, string? Refusal)>
        WrittenAsync(Extraction extraction, ExtractionChoices choices, IReadOnlyList<ExtractionDuplicate> replaced, CancellationToken token)
    {
        var plan = extraction.Plan!;
        var edit = ExtractMethodEdit.Of(plan, extraction.Source, choices);
        if (edit.Refusal is { } refusal)
        {
            return (null, [], EditRefusal(refusal, extraction));
        }

        var main = extraction.Module.Module.AbsoluteUri;
        var edits = new Dictionary<string, List<ExtractionTextEdit>> { [main] = [.. edit.Edits] };
        var replacements = new List<(ExtractionDuplicate Duplicate, ImmutableArray<ExtractionTextEdit> Edits)>();
        foreach (var duplicate in replaced)
        {
            var replacement = duplicate.Replacement(plan, choices);
            var ofModule = edits.TryGetValue(duplicate.Module.AbsoluteUri, out var known) ? known : edits[duplicate.Module.AbsoluteUri] = [];
            if (replacement.Any(each => ofModule.Any(other => each.Range.Start < other.Range.End && other.Range.Start < each.Range.End)))
            {
                return (null, [], Format("ExtractMethodDuplicate_Overlaps", extraction.Where(duplicate)));
            }

            ofModule.AddRange(replacement);
            replacements.Add((duplicate, replacement));
        }

        // the selection's module is edited as the extraction has its text, which a conversion of several routines has changed already.
        var formatting = options.Value.Formatting.ToOptions();
        var written = new Dictionary<string, WrittenModule>();
        foreach (var (address, moduleEdits) in edits)
        {
            var module = extraction.Part.Modules.First(candidate => candidate.Module.AbsoluteUri == address);
            var source = address == main ? extraction.Analysis.Text : module.Source;
            var unformatted = address == main
                ? edit with { Edits = [.. moduleEdits.OrderBy(each => each.Range.Start)] }
                : new ExtractionEdit([.. moduleEdits.OrderBy(each => each.Range.Start)], string.Empty, string.Empty, null, null);
            var formatted = unformatted.Formatted(source, await ParseAsync(module, unformatted.ApplyTo(source), token), formatting);
            var text = formatted.ApplyTo(source);
            written[address] = new WrittenModule(module, formatted, text, await ParseAsync(module, text, token));
        }

        return Verified(extraction, written, replacements, choices.Name) is { } failure ? (null, [], failure) : (written, replacements, null);
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

            var (written, _, failure) = await WrittenAsync(next, choices with { Name = name }, [], token);
            if (written is null)
            {
                return new(null, failure);
            }

            named.Add(name);
            remaining.RemoveAll(converted => converted.Equals(label, StringComparison.OrdinalIgnoreCase));
            var module = written[next.Module.Module.AbsoluteUri];
            if (Reanalyzed(next, module.Text, module.Parse, member, memberKind) is not { } reanalyzed)
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

    // the module analyzed again as a text parses: the procedure, and its selection to come.
    private static Extraction? Reanalyzed(Extraction extraction, string text, SDK.Model.AST.ModuleParseResult parse, string member, MemberKind memberKind)
    {
        if (extraction.Part.Recompose is not { } recompose || parse.SyntaxTree is not { } tree)
        {
            return null;
        }

        var composition = recompose(new Dictionary<string, SDK.Model.AST.ModuleParseResult> { [extraction.Module.Module.AbsoluteUri] = parse });
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

    // the document of a module of the extraction's project.
    private static Uri DocumentOf(Extraction extraction, Uri module)
        => extraction.Part.Modules.First(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri).Document.Id.Uri.ToUri();

    // the ranges of the edits that replace a duplicate, among the edits of its module: indenting them changes their text, not their ranges.
    private static IEnumerable<SourceRange> ReplacementRanges(ExtractionEdit edit, ImmutableArray<ExtractionTextEdit> replacement)
        => edit.Edits.Where(each => replacement.Any(own => own.Range == each.Range && own.Kind == each.Kind)).Select(each => each.Range);

    // the extraction and the replacements verified against the workspace composed again with the modules as the edits leave them: why they are not made, or
    // null.
    private static string? Verified(
        Extraction extraction, IReadOnlyDictionary<string, WrittenModule> written,
        IEnumerable<(ExtractionDuplicate Duplicate, ImmutableArray<ExtractionTextEdit> Edits)> replaced, string name)
    {
        if (extraction.Part.Recompose is not { } recompose)
        {
            return Failed(new(ExtractionVerificationFailureKind.DoesNotParse, null));
        }

        var composition = recompose(written.ToDictionary(module => module.Key, module => module.Value.Parse));
        var after = written.ToDictionary(
            module => module.Key,
            module => ExtractionModuleAnalysis.Of(module.Value.Module.Module, module.Value.Text, module.Value.Parse, composition.Resolver, composition.ScopeTree));

        // the verification analyzes the text the edits leave, which is the one parsed here.
        var main = extraction.Module.Module.AbsoluteUri;
        var source = extraction.Procedure.Declaration;
        if (ExtractionVerification.Verify(extraction.Plan!, written[main].Edit, name, source.Name, source.MemberKind, extraction.Analysis, _ => after[main]) is { } failure)
        {
            return Failed(failure);
        }

        if ((after[main].Procedure(name, MemberKind.Function) ?? after[main].Procedure(name, MemberKind.Procedure)) is not { } extracted)
        {
            return Failed(new(ExtractionVerificationFailureKind.DoesNotParse, name));
        }

        foreach (var (duplicate, edits) in replaced)
        {
            var module = duplicate.Module.AbsoluteUri;
            var call = edits.First(each => each.Kind is ExtractionTextEditKind.Replacement).Range;
            if (ExtractionVerification.VerifyReplacement(duplicate, written[module].Edit, call, name, extraction.AnalysisOf(duplicate.Module)!, after[module], extracted)
                is { } replacementFailure)
            {
                return Format("ExtractMethodDuplicate_NotVerified", extraction.Where(duplicate), Format($"ExtractMethodVerification_{replacementFailure.Kind}", replacementFailure.Detail));
            }
        }

        return null;

        static string Failed(ExtractionVerificationFailure failure)
            => Format("ExtractMethodVerification_Failed", Format($"ExtractMethodVerification_{failure.Kind}", failure.Detail));
    }

    // a text of a module, parsed as its document is: the designer of a form is its document's, which the text of its code does not hold.
    private async Task<SDK.Model.AST.ModuleParseResult> ParseAsync(RenameWorkspace.DocumentModule module, string text, CancellationToken token)
        => await parsing.ParseFragmentAsync(module.Document.Id.Uri.ToUri(), text, token) with { Designer = module.Parse.Designer };

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

    /// <summary>
    /// Whether a refusal is of a range that holds nothing to refactor: no statement to extract, or no <c>GoSub</c> routine at the position, which is on no label
    /// or on one that no <c>GoSub</c> names.
    /// </summary>
    /// <param name="refusal">A notice that refuses the refactoring.</param>
    public static bool FindsNothing(RefactorNotice refusal)
        => refusal.Code == Code(nameof(ExtractionSelectionRefusal.NoStatement))
            || refusal.Code == Code(nameof(GoSubRoutineRefusal.NoLabel))
            || refusal.Code == Code(nameof(GoSubRoutineRefusal.NoGoSub));

    // the accessibility answered, Private when none is; null for an answer that is no accessibility.
    private static ExtractionAccessibility? AccessibilityOf(IReadOnlyList<RefactorAnswer> answers)
        => answers.FirstOrDefault(answer => answer.Id == AccessibilityChoice)?.Value switch
        {
            null => ExtractionAccessibility.Private,
            var value when value.Equals(nameof(ExtractionAccessibility.Private), StringComparison.OrdinalIgnoreCase) => ExtractionAccessibility.Private,
            var value when value.Equals(nameof(ExtractionAccessibility.Public), StringComparison.OrdinalIgnoreCase) => ExtractionAccessibility.Public,
            _ => null,
        };

    // the code of a notice: the same in any language, by which the user accepts a change.
    private static string Code(string kind) => $"extractMethod.{char.ToLowerInvariant(kind[0])}{kind[1..]}";

    private static string EditRefusal(ExtractionEditRefusalKind refusal, Extraction extraction) => refusal is ExtractionEditRefusalKind.NotExtractable
        ? extraction.RefusalMessage(extraction.Plan!.Refusals[0])
        : Format($"ExtractMethodEdit_{refusal}");

    private static string Format(string key, params object?[] arguments)
        => string.Format(CultureInfo.CurrentUICulture, Messages.GetString(key, CultureInfo.CurrentUICulture) ?? key, arguments);

    // a module as the edits of an extraction leave it: the edits, indented, the text they leave, and that text parsed.
    private sealed record class WrittenModule(RenameWorkspace.DocumentModule Module, ExtractionEdit Edit, string Text, SDK.Model.AST.ModuleParseResult Parse);

    // a selection of a procedure of a module, and its plan; null with why the selection is refused.
    private sealed record class Extraction(
        RenameWorkspace.ProjectPart Part, RenameWorkspace.DocumentModule Module, ExtractionModuleAnalysis Analysis, ExtractionProcedureAnalysis Procedure,
        ExtractionSource Source, ExtractionPlan? Plan, RefactorNotice? SelectionRefusal)
    {
        private Uri DocumentAddress => Module.Document.Id.Uri.ToUri();

        public string SuggestedName(ExtractionAccessibility accessibility = ExtractionAccessibility.Private)
            => ExtractionName.Suggest(Plan!, Source, Part.Context, Module.Module, accessibility: accessibility);

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

        private readonly Dictionary<string, ExtractionModuleAnalysis?> _analyses = [];

        // the analysis of a module of the project, as the selection's module is analyzed; the selection's own.
        public ExtractionModuleAnalysis? AnalysisOf(Uri module)
        {
            if (module.AbsoluteUri == Module.Module.AbsoluteUri)
            {
                return Analysis;
            }

            if (!_analyses.TryGetValue(module.AbsoluteUri, out var analysis))
            {
                analysis = Part.Modules.FirstOrDefault(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri) is { } other
                    ? ExtractionModuleAnalysis.Of(other.Module, other.Source, other.Parse, Part.Context.Resolver, Part.Context.ScopeTree)
                    : null;
                _analyses[module.AbsoluteUri] = analysis;
            }

            return analysis;
        }

        // the duplicates a call to the new procedure can replace, by its reach: in the project for a procedure of a standard module, which is Public when one is
        // not in its module; in the class itself for a member of a class module, which is called through an object of the class elsewhere, and which replaces
        // them when it is Public. None for a GoSub routine, which is converted where it is.
        public ImmutableArray<ExtractionDuplicate> Duplicates(ExtractionChoices choices) => Plan!.Selection.Routine is null
            ? ExtractionDuplicates.Find(Plan!, Source, choices, Part.Context, Module.Module,
                Module.Type is ModuleType.StdModule ? [.. Part.Modules.Select(module => module.Module)] : [Module.Module], AnalysisOf)
            : [];

        public bool IsInTheModule(ExtractionDuplicate duplicate) => duplicate.Module.AbsoluteUri == Module.Module.AbsoluteUri;

        // whether the new procedure, so seen, replaces the duplicate: a Private procedure of a standard module those in its module, and a member of a class
        // module those of the class when it is Public.
        public bool Replaces(ExtractionDuplicate duplicate, ExtractionAccessibility accessibility) => Module.Type is ModuleType.StdModule
            ? accessibility is ExtractionAccessibility.Public || IsInTheModule(duplicate)
            : accessibility is ExtractionAccessibility.Public;

        // why the new procedure, Private, does not replace the duplicate.
        public string NeedsPublic(ExtractionDuplicate duplicate) => Module.Type is ModuleType.StdModule
            ? Format("ExtractMethodDuplicate_NeedsPublic", Where(duplicate))
            : Format("ExtractMethodDuplicate_NeedsPublicMember", Where(duplicate));

        // where a duplicate is, and the first line of it.
        public string Where(ExtractionDuplicate duplicate)
        {
            var range = duplicate.Plan.Selection.Range;
            var first = new ExtractionText(duplicate.Source.Text).Of(range).Split('\n')[0].Trim();
            return $"{RenameWorkspace.Locator(Part)(duplicate.Module, range.Start)}: {first}";
        }

        // the duplicates the user replaces: those ticked, or by default those in the module; none when the user replaces none. One that is not a duplicate of the
        // selection as the user's choices extract it, or that only a Public procedure can be called from, is refused, with why.
        public (IReadOnlyList<ExtractionDuplicate> Replaced, string? Refusal) ChosenDuplicates(IReadOnlyList<RefactorAnswer> answers, ExtractionChoices choices)
        {
            if (answers.FirstOrDefault(answer => answer.Id == ReplaceDuplicatesChoice)?.Value is { } replace && bool.TryParse(replace, out var replaces) && !replaces)
            {
                return ([], null);
            }

            var found = Duplicates(choices);
            var items = answers.FirstOrDefault(answer => answer.Id == DuplicatesChoice)?.Items ?? [];
            if (items.FirstOrDefault(item => !found.Any(duplicate => duplicate.Id == item.Id)) is { } unknown)
            {
                return ([], Format("ExtractMethodDuplicate_NotADuplicate", unknown.Id));
            }

            var replaced = found.Where(duplicate =>
                items.FirstOrDefault(item => item.Id == duplicate.Id)?.Answers.FirstOrDefault(each => each.Id == DuplicateReplaceChoice)?.Value is { } ticked
                    && bool.TryParse(ticked, out var isTicked)
                    ? isTicked
                    : Replaces(duplicate, choices.Accessibility)).ToArray();
            return replaced.FirstOrDefault(duplicate => !Replaces(duplicate, choices.Accessibility)) is { } unreachable
                ? ([], NeedsPublic(unreachable))
                : (replaced, null);
        }

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

            choices.Add(new()
            {
                Id = AccessibilityChoice,
                Kind = RefactorChoiceKind.Option,
                Label = Format("ExtractMethodChoice_Accessibility"),
                Description = Format("ExtractMethodChoice_AccessibilityDescription"),
                Default = nameof(ExtractionAccessibility.Private),
                Options =
                [
                    new() { Value = nameof(ExtractionAccessibility.Private), Label = "Private", Description = Format("ExtractMethodChoice_AccessibilityPrivate") },
                    new() { Value = nameof(ExtractionAccessibility.Public), Label = "Public", Description = Format("ExtractMethodChoice_AccessibilityPublic") },
                ],
            });

            // the duplicates of the selection extracted with the defaults: one in another module is replaced only from a Public procedure.
            if (Duplicates(new ExtractionChoices(SuggestedName())) is { IsEmpty: false } duplicates)
            {
                choices.Add(new()
                {
                    Id = ReplaceDuplicatesChoice,
                    Kind = RefactorChoiceKind.Flag,
                    Label = Format("ExtractMethodChoice_ReplaceDuplicates"),
                    Description = Format("ExtractMethodChoice_ReplaceDuplicatesDescription"),
                    Default = "true",
                });
                choices.Add(new()
                {
                    Id = DuplicatesChoice,
                    Kind = RefactorChoiceKind.List,
                    Label = Format("ExtractMethodChoice_Duplicates"),
                    Description = Format("ExtractMethodChoice_DuplicatesDescription"),
                    Items = [.. duplicates.Select(duplicate => new RefactorItem
                    {
                        Id = duplicate.Id,
                        Label = Where(duplicate),
                        Reason = Replaces(duplicate, ExtractionAccessibility.Private)
                            ? null
                            : Format(Module.Type is ModuleType.StdModule ? "ExtractMethodReason_DuplicateNeedsPublic" : "ExtractMethodReason_DuplicateNeedsPublicMember"),
                        Choices =
                        [
                            new()
                            {
                                Id = DuplicateReplaceChoice,
                                Kind = RefactorChoiceKind.Flag,
                                Label = Format("ExtractMethodChoice_DuplicateReplace"),
                                Default = Replaces(duplicate, ExtractionAccessibility.Private) ? "true" : "false",
                            },
                        ],
                    })],
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
