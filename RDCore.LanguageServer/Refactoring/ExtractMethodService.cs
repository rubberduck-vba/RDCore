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
        if (answers.FirstOrDefault(answer => answer.Id == NameChoice)?.Value is not { Length: > 0 } typed
            || await ExtractionOfAsync(documentUri, range, token) is not { Plan: { IsExtractable: true } plan } extraction)
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
        var name = Answer(NameChoice) is { Length: > 0 } typed ? typed.Trim() : extraction.SuggestedName();
        var returned = Answer(ReturnValueChoice) ?? NoReturnValue;
        var returnValue = returned.Length == 0 ? null : plan.ReturnCandidates.FirstOrDefault(candidate => candidate.Name.Equals(returned, StringComparison.OrdinalIgnoreCase));
        if (returned.Length > 0 && returnValue is null)
        {
            return new(null, Format($"ExtractMethodEdit_{ExtractionEditRefusalKind.NotAReturnCandidate}", returned));
        }

        var routeExits = !bool.TryParse(Answer(RouteExitsChoice), out var route) || route;

        // a name that cannot be declared there is refused; one that hides another declaration is the user's to choose.
        if (ExtractionName.Validate(name, plan, extraction.Source, extraction.Part.Context, extraction.Module.Module).FirstOrDefault(problem => !problem.IsWarning) is { } problem)
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

        var choices = new ExtractionChoices(name) { ReturnValue = returnValue?.Variable, RouteExits = routeExits, Parameters = parameters };
        var edit = ExtractMethodEdit.Of(plan, extraction.Source, choices);
        if (edit.Refusal is { } refusal)
        {
            return new(null, EditRefusal(refusal, extraction));
        }

        var text = extraction.Analysis.Text;
        var document = extraction.Module.Document;
        var documentAddress = document.Id.Uri.ToUri();
        var formatted = edit.Formatted(text, await ParseAsync(documentAddress, edit.ApplyTo(text), extraction, token), options.Value.Formatting.ToOptions());
        if (await VerifiedAsync(extraction, formatted, name, documentAddress, token) is { } failure)
        {
            return new(null, Format("ExtractMethodVerification_Failed", Format($"ExtractMethodVerification_{failure.Kind}", failure.Detail)));
        }

        return new(new RefactorWorkspaceEdit
        {
            Documents =
            [
                new RefactorDocumentEdit
                {
                    Uri = documentAddress,
                    Version = document.Version,
                    Edits = [.. formatted.Edits.Select(each => new RefactorTextEdit { Range = each.Range, NewText = each.NewText })],
                },
            ],
        }, null);
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
