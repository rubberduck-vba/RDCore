using Microsoft.Extensions.Options;
using OmniSharp.Extensions.LanguageServer.Protocol;
using RDCore.LanguageServer.Parsing;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.SDK.Model.AST.Declarations;
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
internal interface IExtractMethodService
{
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
    /// The id of the choice to replace a duplicate, among the choices of an item of <see cref="DuplicatesChoice"/>: ticked by default for one in the module,
    /// which a <c>Private</c> procedure can be called from.
    /// </summary>
    public const string DuplicateReplaceChoice = "replace";

    // the value of the option that returns nothing: no variable is named so.
    private const string NoReturnValue = "";

    private static readonly ResourceManager Messages = new("RDCore.LanguageServer.Services.Refactoring.ExtractMethod.ExtractMethodMessages", typeof(ExtractMethodService).Assembly);

    /// <summary>
    /// The title of the code action that extracts a selection, in the language of the user interface.
    /// </summary>
    public static string Title() => Format("ExtractMethod_Title");

    /// <summary>
    /// Why an extraction offered for one version of a document is not made for another, in the language of the user interface.
    /// </summary>
    public static string DocumentChanged() => Format("ExtractMethod_DocumentChanged");

    public async Task<RefactorPrepareResult> PrepareAsync(Uri documentUri, SourceRange range, CancellationToken token)
    {
        if (await ExtractionOfAsync(documentUri, range, token) is not { } extraction)
        {
            var kind = nameof(ExtractionSelectionRefusal.NoStatement);
            return new RefactorPrepareResult
            {
                Notices = [new RefactorNotice { Code = Code(kind), Kind = RefactorNoticeKind.Refusal, Message = Format($"ExtractMethodSelection_{kind}") }],
            };
        }

        if (extraction.Plan is not { } plan)
        {
            return new RefactorPrepareResult { Notices = [extraction.SelectionRefusal!] };
        }

        // from here the selection snapped: the statements it is of are what the editor shows will move, refused or not.
        var notices = extraction.Notices();
        var moved = plan.Selection.Range;
        if (!plan.IsExtractable)
        {
            return new RefactorPrepareResult { Notices = notices, Range = moved };
        }

        // a refusal of the writer that no answer can lift is said before the user answers anything.
        var written = ExtractMethodEdit.Of(plan, extraction.Source, new ExtractionChoices(extraction.SuggestedName()));
        if (written.Refusal is ExtractionEditRefusalKind.ExitByJumpTable)
        {
            return new RefactorPrepareResult
            {
                Notices = [new RefactorNotice { Code = Code(written.Refusal.Value.ToString()), Kind = RefactorNoticeKind.Refusal, Message = EditRefusal(written.Refusal.Value, extraction) }, .. notices],
                Range = moved,
            };
        }

        return new RefactorPrepareResult { Choices = extraction.Choices(), Notices = notices, Range = moved };
    }

    public async Task<RefactorIdentifierCheckResult> CheckIdentifierAsync(Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, CancellationToken token)
    {
        // the name as ApplyAsync reads it: trimmed, and one that is not answered takes its default, which raises nothing.
        // the name is checked as declared with the accessibility answered, and as Private when that is none.
        if (answers.FirstOrDefault(answer => answer.Id == NameChoice)?.Value is not { Length: > 0 } typed
            || await ExtractionOfAsync(documentUri, range, token) is not { Plan: { IsExtractable: true } plan } extraction)
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
        Uri documentUri, SourceRange range, IReadOnlyList<RefactorAnswer> answers, IReadOnlyList<string> accepted, CancellationToken token)
    {
        if (await ExtractionOfAsync(documentUri, range, token) is not { } extraction)
        {
            return new(null, Format("ExtractMethodSelection_NoStatement"));
        }

        if (extraction.Plan is not { } plan)
        {
            return new(null, extraction.SelectionRefusal!.Message);
        }

        if (!plan.IsExtractable)
        {
            return new(null, extraction.RefusalMessage(plan.Refusals[0]));
        }

        // the answers, each to its choice; one that is not given takes the choice's default.
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
            return new(null, Format($"ExtractMethodEdit_{ExtractionEditRefusalKind.NotAReturnCandidate}", returned));
        }

        var routeExits = !bool.TryParse(Answer(RouteExitsChoice), out var route) || route;

        // a name that cannot be declared there is refused; one that hides another declaration is the user's to choose.
        if (ExtractionName.Validate(name, plan, extraction.Source, extraction.Part.Context, extraction.Module.Module, accessibility)
            .FirstOrDefault(problem => !problem.IsWarning) is { } problem)
        {
            return new(null, problem.ToMessage(RenameWorkspace.Locator(extraction.Part)));
        }

        // a change of what the code does is made only when the user accepts it.
        if (plan.BehaviourChanges.Where(change => !change.IsNote).FirstOrDefault(change => !accepted.Contains(Code(change.Kind.ToString()))) is { } declined)
        {
            return new(null, Format("ExtractMethodEdit_NotAccepted", extraction.ChangeMessage(declined)));
        }

        // the parameters as the user renamed them, put them in order and passes them; an item that is not answered is kept as it is, where it is.
        var parameterAnswers = answers.FirstOrDefault(answer => answer.Id == ParametersChoice)?.Items
            .Select(item => new ExtractionParameterAnswer(item.Id,
                item.Answers.FirstOrDefault(each => each.Id == ParameterNameChoice)?.Value,
                item.Answers.FirstOrDefault(each => each.Id == ParameterPassingChoice)?.Value))
            .ToArray() ?? [];
        var (parameters, parameterProblem) = ExtractionParameters.Choose(parameterAnswers, plan, extraction.Source, name, extraction.Part.Context, extraction.Module.Module);
        if (parameterProblem is not null)
        {
            return new(null, parameterProblem.ToMessage());
        }

        var choices = new ExtractionChoices(name) { ReturnValue = returnValue?.Variable, RouteExits = routeExits, Parameters = parameters, Accessibility = accessibility };
        var edit = ExtractMethodEdit.Of(plan, extraction.Source, choices);
        if (edit.Refusal is { } refusal)
        {
            return new(null, EditRefusal(refusal, extraction));
        }

        // the duplicates the user has replaced, and the edits of each module: the selection's, and those that replace each duplicate in it.
        var (replaced, duplicateRefusal) = extraction.ChosenDuplicates(answers, choices);
        if (duplicateRefusal is not null)
        {
            return new(null, duplicateRefusal);
        }

        var edits = new Dictionary<string, List<ExtractionTextEdit>> { [extraction.Module.Module.AbsoluteUri] = [.. edit.Edits] };
        var replacements = new List<(ExtractionDuplicate Duplicate, ImmutableArray<ExtractionTextEdit> Edits)>();
        foreach (var duplicate in replaced)
        {
            var replacement = duplicate.Replacement(plan, choices);
            var ofModule = edits.TryGetValue(duplicate.Module.AbsoluteUri, out var known) ? known : edits[duplicate.Module.AbsoluteUri] = [];
            if (replacement.Any(each => ofModule.Any(other => each.Range.Start < other.Range.End && other.Range.Start < each.Range.End)))
            {
                return new(null, Format("ExtractMethodDuplicate_Overlaps", extraction.Where(duplicate)));
            }

            ofModule.AddRange(replacement);
            replacements.Add((duplicate, replacement));
        }

        // each module's edits, indented as the user's settings say.
        var formatting = options.Value.Formatting.ToOptions();
        var written = new Dictionary<string, (RenameWorkspace.DocumentModule Module, ExtractionEdit Edit, string Text, SDK.Model.AST.ModuleParseResult Parse)>();
        foreach (var (address, moduleEdits) in edits)
        {
            var module = extraction.Part.Modules.First(candidate => candidate.Module.AbsoluteUri == address);
            var unformatted = address == extraction.Module.Module.AbsoluteUri
                ? edit with { Edits = [.. moduleEdits.OrderBy(each => each.Range.Start)] }
                : new ExtractionEdit([.. moduleEdits.OrderBy(each => each.Range.Start)], string.Empty, string.Empty, null, null);
            var formatted = unformatted.Formatted(module.Source, await ParseAsync(module, unformatted.ApplyTo(module.Source), token), formatting);
            var text = formatted.ApplyTo(module.Source);
            written[address] = (module, formatted, text, await ParseAsync(module, text, token));
        }

        if (Verified(extraction, written, replacements, name) is { } failure)
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

    // the document of a module of the extraction's project.
    private static Uri DocumentOf(Extraction extraction, Uri module)
        => extraction.Part.Modules.First(candidate => candidate.Module.AbsoluteUri == module.AbsoluteUri).Document.Id.Uri.ToUri();

    // the ranges of the edits that replace a duplicate, among the edits of its module: indenting them changes their text, not their ranges.
    private static IEnumerable<SourceRange> ReplacementRanges(ExtractionEdit edit, ImmutableArray<ExtractionTextEdit> replacement)
        => edit.Edits.Where(each => replacement.Any(own => own.Range == each.Range && own.Kind == each.Kind)).Select(each => each.Range);

    // the extraction and the replacements verified against the workspace composed again with the modules as the edits leave them: why they are not made, or
    // null.
    private static string? Verified(
        Extraction extraction,
        IReadOnlyDictionary<string, (RenameWorkspace.DocumentModule Module, ExtractionEdit Edit, string Text, SDK.Model.AST.ModuleParseResult Parse)> written,
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

    // the module, the procedure and the selection a range is of; null when it is of no procedure of a module of the workspace.
    private async Task<Extraction?> ExtractionOfAsync(Uri documentUri, SourceRange range, CancellationToken token)
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

    // a selection of a procedure of a module, and its plan; null with why the selection is refused.
    private sealed record class Extraction(
        RenameWorkspace.ProjectPart Part, RenameWorkspace.DocumentModule Module, ExtractionModuleAnalysis Analysis, ExtractionProcedureAnalysis Procedure,
        ExtractionSource Source, ExtractionPlan? Plan, RefactorNotice? SelectionRefusal)
    {
        private Uri DocumentAddress => Module.Document.Id.Uri.ToUri();

        public string SuggestedName(ExtractionAccessibility accessibility = ExtractionAccessibility.Private)
            => ExtractionName.Suggest(Plan!, Source, Part.Context, Module.Module, accessibility: accessibility);

        public string Where(SourceLocation? location) => location is { } at ? RenameWorkspace.Locator(Part)(Module.Module, at.Range.Start) : string.Empty;

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

        // the duplicates a call to the new procedure can replace, by its reach: none for a member of a class module, which is called through an object of the
        // class; in the project for a procedure of a standard module, which is Public when one is not in its module.
        public ImmutableArray<ExtractionDuplicate> Duplicates(ExtractionChoices choices) => Module.Type is ModuleType.StdModule
            ? ExtractionDuplicates.Find(Plan!, Source, choices, Part.Context, Module.Module, [.. Part.Modules.Select(module => module.Module)], AnalysisOf)
            : [];

        public bool IsInTheModule(ExtractionDuplicate duplicate) => duplicate.Module.AbsoluteUri == Module.Module.AbsoluteUri;

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
                    : IsInTheModule(duplicate)).ToArray();
            return choices.Accessibility is ExtractionAccessibility.Private && replaced.FirstOrDefault(duplicate => !IsInTheModule(duplicate)) is { } outside
                ? ([], Format("ExtractMethodDuplicate_NeedsPublic", Where(outside)))
                : (replaced, null);
        }

        public string RefusalMessage(ExtractionRefusal refusal) => Format($"ExtractMethodRefusal_{refusal.Kind}", refusal.Name, Where(refusal.Location));

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

            if (plan.ReturnCandidates is { IsEmpty: false } candidates)
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
                        Reason = IsInTheModule(duplicate) ? null : Format("ExtractMethodReason_DuplicateNeedsPublic"),
                        Choices =
                        [
                            new()
                            {
                                Id = DuplicateReplaceChoice,
                                Kind = RefactorChoiceKind.Flag,
                                Label = Format("ExtractMethodChoice_DuplicateReplace"),
                                Default = IsInTheModule(duplicate) ? "true" : "false",
                            },
                        ],
                    })],
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
