using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Source;
using RDCore.LanguageServer.Services.Refactoring.ExtractMethod;
using RDCore.Parsing;
using RDCore.Runtime.LanguageCore.Refactoring;
using RDCore.SDK.Formatting;
using RDCore.Tests.Semantics.Flow;

namespace RDCore.Tests.Semantics.Refactoring;

/// <summary>
/// The plan of extracting lines of a procedure <c>Run</c>, as <see cref="FlowFixture"/> analyzes it, and the extraction written.
/// </summary>
internal static class ExtractionFixture
{
    /// <summary>
    /// The plan of extracting the lines of the body from <paramref name="first"/> to <paramref name="last"/> (zero-based).
    /// </summary>
    public static ExtractionPlan Plan(
        string body, int first, int last, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub", bool optionExplicit = true, string declarations = "")
        => Plan(body, new SourcePosition(first, 0), new SourcePosition(last, 1), header, end, optionExplicit, declarations);

    /// <summary>
    /// The plan of extracting the body from <paramref name="start"/> to <paramref name="end"/>, lines zero-based from the body's first.
    /// </summary>
    public static ExtractionPlan Plan(
        string body, SourcePosition start, SourcePosition end, string header = "Public Sub Run(ByVal n As Long)", string close = "End Sub", bool optionExplicit = true,
        string declarations = "")
        => PlanOf(FlowFixture.Analyze(body, header, close, optionExplicit, declarations), start, end);

    // The module after extracting the lines of the body from first to last (zero-based) as a procedure name, returning the variable returnValue
    // names; and the edit. With formatting, the edit is formatted as it says. A range, lines zero-based from the body's first, is extracted instead of the lines.
    // The parameters are answered as parameters says, which must be valid answers.
    public static (string Module, ExtractionEdit Edit) Extract(
        string body, int first, int last, string name = "Extracted", string? returnValue = null, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub",
        bool optionExplicit = true, string declarations = "", bool routeExits = true, CodeFormattingOptions? formatting = null, SourceRange? range = null,
        IReadOnlyList<ExtractionParameterAnswer>? parameters = null)
    {
        var extracted = Extracted(body, first, last, name, returnValue, header, end, optionExplicit, declarations, routeExits, formatting, range: range, parameters: parameters);
        return (extracted.Module, extracted.Edit);
    }

    // Extract's module and edit, with the module before the extraction and the plan the edit was written from; the workspace has the class modules in
    // classes beside the module, which is a class module itself with classModule.
    public static (string Original, string Module, ExtractionEdit Edit, ExtractionPlan Plan) Extracted(
        string body, int first, int last, string name = "Extracted", string? returnValue = null, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub",
        bool optionExplicit = true, string declarations = "", bool routeExits = true, CodeFormattingOptions? formatting = null,
        IReadOnlyList<(string Name, string Source)>? classes = null, bool classModule = false, SourceRange? range = null,
        IReadOnlyList<ExtractionParameterAnswer>? parameters = null)
    {
        var analyzed = FlowFixture.Analyze(body, header, end, optionExplicit, declarations, classes, classModule);
        var plan = PlanOf(analyzed, range?.Start ?? new SourcePosition(first, 0), range?.End ?? new SourcePosition(last, 1));
        var source = new ExtractionSource(analyzed.Module, analyzed.Procedure, analyzed.Model.Procedure, analyzed.Text, analyzed.Scopes);
        var (chosen, problem) = ExtractionParameters.Choose(parameters ?? [], plan, source, name, analyzed.Context, analyzed.ModuleAddress);
        Assert.IsNull(problem, problem?.ToMessage());
        var choices = new ExtractionChoices(name) { ReturnValue = returnValue is null ? null : analyzed.Variable(returnValue), RouteExits = routeExits, Parameters = chosen };
        var edit = ExtractMethodEdit.Of(plan, source, choices);
        if (formatting is not null)
        {
            var edited = new ModuleParser().Parse(FlowFixture.FileOf(classModule), edit.ApplyTo(analyzed.Text));
            edit = edit.Formatted(analyzed.Text, edited, formatting);
        }

        return (analyzed.Text, edit.ApplyTo(analyzed.Text), edit, plan);
    }

    // The verification of extracting the lines first to last as Extracted, formatted as for a user who set nothing; tamper, when given, changes the edit before it
    // is verified, as a writer that got it wrong would have written it. The parameters are answered as parameters says.
    public static ExtractionVerificationFailure? Verified(
        string body, int first, int last, string? returnValue = null, string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub",
        bool optionExplicit = true, string declarations = "", Func<ExtractionEdit, ExtractionEdit>? tamper = null,
        IReadOnlyList<ExtractionParameterAnswer>? parameters = null)
    {
        var extracted = Extracted(body, first, last, returnValue: returnValue, header: header, end: end, optionExplicit: optionExplicit, declarations: declarations,
            formatting: CodeFormattingOptions.Default, parameters: parameters);
        Assert.IsNull(extracted.Edit.Refusal, $"refused: {extracted.Edit.Refusal}");
        var edit = tamper?.Invoke(extracted.Edit) ?? extracted.Edit;
        var before = FlowFixture.Module(extracted.Original);
        var kind = before.Parse.SyntaxTree!.Children.OfType<MemberDeclarationNode>().First(member => member.Name == "Run").MemberKind;
        return ExtractionVerification.Verify(extracted.Plan, edit, "Extracted", "Run", kind, before, text => FlowFixture.Module(text));
    }

    // the edit with the text of its edits of the kind changed from what to what instead.
    public static ExtractionEdit Replacing(ExtractionEdit edit, ExtractionTextEditKind kind, string what, string instead)
    {
        Assert.IsTrue(edit.Edits.Any(change => change.Kind == kind && change.NewText.Contains(what, StringComparison.Ordinal)), $"no {kind} edit writes {what}");
        return edit with
        {
            Edits = [.. edit.Edits.Select(change => change.Kind == kind ? change with { NewText = change.NewText.Replace(what, instead, StringComparison.Ordinal) } : change)],
        };
    }

    // What a name for the procedure extracted from the lines first to last is checked against.
    public static (ExtractionPlan Plan, ExtractionSource Source, RenameContext Context, Uri Module, FlowFixture.Analyzed Analyzed) Naming(
        string body, int first, int last, string declarations = "", string header = "Public Sub Run(ByVal n As Long)", string end = "End Sub")
    {
        var analyzed = FlowFixture.Analyze(body, header, end, declarations: declarations);
        var plan = PlanOf(analyzed, new SourcePosition(first, 0), new SourcePosition(last, 1));
        var source = new ExtractionSource(analyzed.Module, analyzed.Procedure, analyzed.Model.Procedure, analyzed.Text, analyzed.Scopes);
        return (plan, source, analyzed.Context, analyzed.ModuleAddress, analyzed);
    }

    private static ExtractionPlan PlanOf(FlowFixture.Analyzed analyzed, SourcePosition start, SourcePosition end)
    {
        var requested = new SourceRange(analyzed.FirstBodyLine + start.Line, start.Character, analyzed.FirstBodyLine + end.Line, end.Character);
        Assert.IsTrue(ExtractionSelection.TrySnap(analyzed.Procedure, analyzed.Instructions, analyzed.Directives, requested, out var selection, out var refusal, out _), $"refused: {refusal}");
        return ExtractionAnalysis.Plan(selection!, analyzed.Graph, analyzed.Accesses, analyzed.Model, analyzed.Scopes);
    }
}
