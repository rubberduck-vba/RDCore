using RDCore.SDK.Formatting;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.StdLib;
using System.Collections.Immutable;

namespace RDCore.LanguageServer.Services.Refactoring.ExtractMethod;

/// <summary>
/// What an edit of the extraction writes, which a preview shows apart.
/// </summary>
internal enum ExtractionTextEditKind
{
    /// <summary>
    /// A declaration statement outside the selection, written again without the variables whose declarations move into the new procedure.
    /// </summary>
    Declaration,

    /// <summary>
    /// What replaces the selection: the declarations that stay in the source procedure, and the call.
    /// </summary>
    Call,

    /// <summary>
    /// The new procedure, after the source procedure.
    /// </summary>
    Procedure,

    /// <summary>
    /// The <c>Enum</c> of the exit codes, before the module's first procedure.
    /// </summary>
    Enumeration,

    /// <summary>
    /// A call that replaces a <c>GoSub</c> of a routine converted to a procedure, or the <c>Select Case</c> with a call that replaces an <c>On…GoSub</c> of it.
    /// </summary>
    Site,

    /// <summary>
    /// A call that replaces a duplicate of the selection (<see cref="ExtractionDuplicate"/>).
    /// </summary>
    Replacement,
}

/// <summary>
/// How far the new procedure is seen.
/// </summary>
internal enum ExtractionAccessibility
{
    /// <summary>
    /// <c>Private</c>: in its module only. The default.
    /// </summary>
    Private,

    /// <summary>
    /// <c>Public</c>: in the project, for a procedure of a standard module; for a member of a class module, through each of its objects. The <c>Enum</c> of its
    /// exit codes is <c>Public</c> too, and the project's: a public member's type is never a private one.
    /// </summary>
    Public,
}

/// <summary>
/// A change of the text of a module: what is written in a range, written instead.
/// </summary>
/// <param name="Range">What is replaced; an empty range inserts.</param>
/// <param name="NewText">What is written there.</param>
/// <param name="Kind">What it writes.</param>
/// <param name="CallStart">Where the call starts in <paramref name="NewText"/>, for a <see cref="ExtractionTextEditKind.Site"/> that writes more than the call.</param>
/// <param name="CallLength">How long the call is from <paramref name="CallStart"/>; -1 when the call is all of <paramref name="NewText"/>.</param>
internal readonly record struct ExtractionTextEdit(SourceRange Range, string NewText, ExtractionTextEditKind Kind, int CallStart = 0, int CallLength = -1);

/// <summary>
/// The module a selection is extracted from: its syntax, its text, and what the procedure the selection is in is.
/// </summary>
/// <param name="Module">The module's syntax tree.</param>
/// <param name="Procedure">The procedure the selection is in.</param>
/// <param name="ProcedureId">The procedure's own identity: what its result is bound to.</param>
/// <param name="Text">The module's text, which the syntax tree's positions are of.</param>
/// <param name="Scopes">The scopes of the workspace, which say what each variable is declared as.</param>
internal sealed record class ExtractionSource(ModuleNode Module, MemberDeclarationNode Procedure, SemanticId ProcedureId, string Text, ScopeTree Scopes);

/// <summary>
/// What the user chose of an extraction.
/// </summary>
/// <param name="Name">The name of the new procedure, which is valid (<see cref="ExtractionName.Validate"/>).</param>
internal sealed record class ExtractionChoices(string Name)
{
    /// <summary>
    /// The variable the new procedure returns, one of the plan's <see cref="ExtractionPlan.ReturnCandidates"/>; <see langword="null"/> for none, and then a
    /// procedure that has no exit to route is a <c>Sub</c>.
    /// </summary>
    public SemanticId? ReturnValue { get; init; }

    /// <summary>
    /// Whether a selection that leaves the source procedure, or a loop or a block around it, is extracted with its exits routed: the new procedure returns an exit
    /// code, and the call site goes where it says. On by default; a selection with an exit is refused when it is off.
    /// </summary>
    public bool RouteExits { get; init; } = true;

    /// <summary>
    /// The parameters as the user chose them (<see cref="ExtractionParameters.Choose"/>), in the order the new procedure declares them; empty for the plan's,
    /// as <see cref="ExtractionParameters.InDeclarationOrder"/> puts them.
    /// </summary>
    public ImmutableArray<ExtractionParameterChoice> Parameters { get; init; } = [];

    /// <summary>
    /// How far the new procedure is seen; <see cref="ExtractionAccessibility.Private"/> by default.
    /// </summary>
    public ExtractionAccessibility Accessibility { get; init; }
}

/// <summary>
/// Why an extraction is not written, once the plan allows it.
/// </summary>
internal enum ExtractionEditRefusalKind
{
    /// <summary>
    /// The plan refuses the selection (<see cref="ExtractionPlan.Refusals"/>).
    /// </summary>
    NotExtractable,

    /// <summary>
    /// The return value chosen is none of the plan's <see cref="ExtractionPlan.ReturnCandidates"/>.
    /// </summary>
    NotAReturnCandidate,

    /// <summary>
    /// The selection leaves the source procedure, or a loop or a block around it, and the user turned exit routing off (<see cref="ExtractionChoices.RouteExits"/>).
    /// </summary>
    ExitRoutingDeclined,

    /// <summary>
    /// A <c>GoSub</c> routine converted to a procedure reads or writes the source procedure's result, which each of its calls would have to copy.
    /// </summary>
    RoutineUsesTheResult,

    /// <summary>
    /// A <c>GoSub</c> routine converted to a procedure runs itself: a <c>GoSub</c> of it is written in it.
    /// </summary>
    RoutineRunsItself,

    /// <summary>
    /// A <c>GoSub</c> routine converted to a procedure uses the object of a <c>With</c> block it is written in.
    /// </summary>
    RoutineInAWithBlock,
}

/// <summary>
/// The extraction, written: the edits of the module, and the texts they are made of, which a preview shows.
/// </summary>
/// <param name="Edits">The edits of the module, in the order of the text, none of which overlaps another; empty when <see cref="Refusal"/> says why.</param>
/// <param name="Procedure">The new procedure, as it is inserted after the source procedure.</param>
/// <param name="Call">What replaces the selection: the declarations that stay in the source, and the call.</param>
/// <param name="Enumeration">The <c>Enum</c> of the exit codes, inserted before the module's first procedure; <see langword="null"/> when there is none.</param>
/// <param name="Refusal">Why nothing is written, when nothing is.</param>
internal sealed record class ExtractionEdit(ImmutableArray<ExtractionTextEdit> Edits, string Procedure, string Call, string? Enumeration, ExtractionEditRefusalKind? Refusal)
{
    /// <summary>
    /// The module's text with the edits made.
    /// </summary>
    /// <param name="text">The module's text, which the edits are of.</param>
    public string ApplyTo(string text) => ExtractionText.Apply(text, Edits).Text;

    /// <summary>
    /// The extraction with the lines it writes indented by the code formatter, as the module's other lines are: the lines it leaves as they were stay so.
    /// </summary>
    /// <param name="text">The module's text, which the edits are of.</param>
    /// <param name="edited">The module as it is parsed from <see cref="ApplyTo"/> of <paramref name="text"/>.</param>
    /// <param name="options">How the code is indented.</param>
    public ExtractionEdit Formatted(string text, ModuleParseResult edited, CodeFormattingOptions options)
    {
        if (Refusal is not null)
        {
            return this;
        }

        var (module, written) = ExtractionText.Apply(text, Edits);
        var lines = new ExtractionText(module);

        // a line the edit writes from its start is indented; the first line of an edit that starts after other code keeps that code's indentation.
        var indentation = Enumerable.Range(0, Edits.Length)
            .Where(index => Edits[index].NewText.Length > 0)
            .SelectMany(index => ModuleIndenter.Indent(edited, module, options, lines.LineOf(written[index].Start), lines.LineOf(written[index].End)))
            .Distinct()
            .Select(indent => (Start: lines.OffsetOf(new SourcePosition(indent.Line, indent.Character)), End: lines.OffsetOf(new SourcePosition(indent.EndLine, indent.EndCharacter)), indent.NewText))
            .ToArray();

        var edits = Edits.Select((edit, index) =>
        {
            var (start, end) = written[index];
            var formatted = edit.NewText;
            var (callStart, callLength) = (edit.CallStart, edit.CallLength);
            // the line that follows an edit ending with a line break is not the edit's.
            foreach (var indent in indentation.Where(indent => indent.Start >= start && indent.Start < end && indent.End <= end).OrderByDescending(indent => indent.Start))
            {
                formatted = string.Concat(formatted.AsSpan(0, indent.Start - start), indent.NewText, formatted.AsSpan(indent.End - start));

                // the call of a site that writes more than the call moves with the indentation before it, and spans the indentation of its own lines.
                var moved = indent.NewText.Length - (indent.End - indent.Start);
                if (callLength >= 0 && indent.Start - start < edit.CallStart)
                {
                    callStart += moved;
                }
                else if (callLength >= 0 && indent.Start - start < edit.CallStart + edit.CallLength)
                {
                    callLength += moved;
                }
            }

            return edit with { NewText = formatted, CallStart = callStart, CallLength = callLength };
        }).ToImmutableArray();

        string TextOf(ExtractionTextEditKind kind) => edits.FirstOrDefault(edit => edit.Kind == kind).NewText ?? string.Empty;
        return this with
        {
            Edits = edits,
            Procedure = TextOf(ExtractionTextEditKind.Procedure).TrimStart('\r', '\n'),
            Call = TextOf(ExtractionTextEditKind.Call),
            Enumeration = Enumeration is null ? null : TextOf(ExtractionTextEditKind.Enumeration).TrimEnd('\r', '\n'),
        };
    }

    internal static ExtractionEdit Refused(ExtractionEditRefusalKind refusal) => new([], string.Empty, string.Empty, null, refusal);
}

/// <summary>
/// Writes the extraction a plan says: the new procedure, <c>Private</c> or <c>Public</c> as the user chose, after the source procedure; the call that replaces the
/// selection; the declarations that move; and the routing of the exits.
/// </summary>
/// <remarks>
/// <para>
/// The new procedure is a <c>Function</c> when it returns a value or an exit code, and a <c>Sub</c> otherwise. Its parameters are the plan's, in the order and
/// with the names and the passing the user chose (<see cref="ExtractionChoices.Parameters"/>), and typed as its variable is declared: an array's with
/// <c>()</c>, a fixed-length string's as <c>String</c>, an object's without <c>New</c>. Its body is the selection, with the constants it reads and the locals it
/// has of its own declared first, and <c>On Error Resume Next</c> when the plan says it starts with it. A parameter the user renamed is named so wherever the
/// selection names its variable.
/// </para>
/// <para>
/// The call is <c>Name a, b</c>, never <c>Name (a)</c>, which passes a copy; or <c>x = Name(a, b)</c>, <c>Set</c> for an object. A selection that leaves the source
/// procedure, or a loop or a block around it, returns a code: <c>True</c> for one exit, a member of an <c>Enum</c> for several, as public as the procedure is.
/// The call site goes where the code says. An <c>On…GoTo</c> stays as it is written, its selector evaluated as before; each of its labels outside the selection is
/// replaced with a label after the new procedure's body, which returns that label's exit code. The source's result, read or written in the selection, is given to
/// the new procedure as a copy, <c>ByRef</c>, and written back after the call: a function's name cannot be passed.
/// </para>
/// <para>
/// The text is indented as the selection was, with the body of the new procedure one level in: <see cref="ExtractionEdit.Formatted"/> indents it as the user's
/// settings say.
/// </para>
/// </remarks>
internal static class ExtractMethodEdit
{
    private const string Indent = "    ";

    /// <summary>
    /// Writes the extraction.
    /// </summary>
    /// <param name="plan">What the extraction takes.</param>
    /// <param name="source">The module, and the procedure the selection is in.</param>
    /// <param name="choices">What the user chose.</param>
    public static ExtractionEdit Of(ExtractionPlan plan, ExtractionSource source, ExtractionChoices choices)
    {
        if (!plan.IsExtractable)
        {
            return ExtractionEdit.Refused(ExtractionEditRefusalKind.NotExtractable);
        }

        if (choices.ReturnValue is { } chosen && !plan.ReturnCandidates.Any(candidate => candidate.Variable == chosen))
        {
            return ExtractionEdit.Refused(ExtractionEditRefusalKind.NotAReturnCandidate);
        }

        if (!plan.Exits.IsEmpty && !choices.RouteExits)
        {
            return ExtractionEdit.Refused(ExtractionEditRefusalKind.ExitRoutingDeclined);
        }

        if (plan.Selection.Routine is { } routine)
        {
            // each GoSub becomes a statement that calls the procedure, which returns no value: a Return leaves it as Exit Sub does.
            if (choices.ReturnValue is not null)
            {
                return ExtractionEdit.Refused(ExtractionEditRefusalKind.NotAReturnCandidate);
            }

            var refusal = (plan.AssignsResult || plan.ReadsResult, plan.WithTarget is not null) switch
            {
                (true, _) => ExtractionEditRefusalKind.RoutineUsesTheResult,
                (_, true) => ExtractionEditRefusalKind.RoutineInAWithBlock,
                _ when routine.Sites.Select(site => site.SourceLocation.Range).Concat(routine.Tables.Select(table => table.SourceLocation.Range))
                        .Any(site => site.Start >= plan.Selection.Range.Start && site.End <= plan.Selection.Range.End)
                    => ExtractionEditRefusalKind.RoutineRunsItself,
                _ => (ExtractionEditRefusalKind?)null,
            };
            if (refusal is { } refused)
            {
                return ExtractionEdit.Refused(refused);
            }
        }

        return new Writer(plan, source, choices, new ExtractionText(source.Text)).Write();
    }

    // the names the extraction declares in the module: the new procedure's, and the enumeration of its exit codes with its members when it has several.
    internal static IEnumerable<string> ModuleNamesOf(ExtractionPlan plan, ExtractionSource source, string name)
    {
        var exits = new Writer(plan, source, new ExtractionChoices(name), new ExtractionText(source.Text)).ExitCodes();
        return exits.Count > 1 ? [name, $"{name}Exit", $"{name}ExitNone", .. exits.Select(exit => exit.Code)] : [name];
    }

    // the names the new procedure has besides its parameters: its own and those of its exit codes, those of its locals, the copy of the source's result, and
    // the object of a With block.
    internal static IEnumerable<string> NewProcedureNamesOf(ExtractionPlan plan, ExtractionSource source, string name)
    {
        var writer = new Writer(plan, source, new ExtractionChoices(name), new ExtractionText(source.Text));
        return ModuleNamesOf(plan, source, name)
            .Concat(plan.Locals.Select(local => local.Name))
            .Concat(plan.WithTarget is { } target ? [target.Name] : [])
            .Concat(plan.AssignsResult || plan.ReadsResult ? [writer.ResultCopyName()] : []);
    }

    // a parameter of the plan as the new procedure's signature writes it (ByRef total As Long).
    internal static string ParameterTextOf(ExtractionPlan plan, ExtractionSource source, ExtractionParameter parameter)
        => new Writer(plan, source, new ExtractionChoices(string.Empty), new ExtractionText(source.Text)).ParameterText(parameter);

    // what a variable, a parameter or a constant of the source procedure is declared as, after its name: its type-declaration character, its bounds, its As
    // clause, its value (& for `n&`, `(1 To 3) As Long`, ` As Long = 5`); the name of the type it has when nothing declares it.
    internal static string DeclaredTypeOf(ExtractionPlan plan, ExtractionSource source, SemanticId variable)
        => new Writer(plan, source, new ExtractionChoices(string.Empty), new ExtractionText(source.Text)).DeclaredType(variable);

    // the exit codes the new procedure returns, each with what the call site does for it.
    internal static IEnumerable<(string Code, string Dispatch)> ExitCodesOf(ExtractionPlan plan, ExtractionSource source, string name)
        => new Writer(plan, source, new ExtractionChoices(name), new ExtractionText(source.Text)).ExitCodes().Select(exit => (exit.Code, exit.Dispatch));

    private sealed class Writer(ExtractionPlan plan, ExtractionSource source, ExtractionChoices choices, ExtractionText text)
    {
        private readonly string _name = choices.Name;
        private readonly string _newLine = text.NewLine;
        private readonly List<VariableDeclarationNode> _variables = [.. source.Procedure.Descendants().OfType<VariableDeclarationNode>()];
        private readonly List<ConstantDeclarationNode> _constants = [.. source.Procedure.Descendants().OfType<ConstantDeclarationNode>()];
        private readonly List<ParameterDeclarationNode> _parameters = [.. source.Procedure.Children.OfType<ParameterDeclarationNode>()];

        // the parameters in the order the new procedure declares them, and what the user chose of each.
        private readonly ExtractionParameter[] _ordered = choices.Parameters.IsEmpty
            ? [.. ExtractionParameters.InDeclarationOrder(plan)]
            : [.. choices.Parameters.Select(choice => plan.Parameters.Single(parameter => parameter.Variable == choice.Variable))];

        private ExtractionParameterChoice? ChoiceOf(SemanticId variable) => choices.Parameters.FirstOrDefault(choice => choice.Variable == variable);

        // the return value is the function's own when there is no exit code to return instead: with one, it goes out ByRef, as a parameter.
        private SemanticId? ReturnValue => plan.Exits.IsEmpty ? choices.ReturnValue : null;

        private string ProcedureKeyword => source.Procedure.MemberKind switch
        {
            MemberKind.Function => "Function",
            MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet => "Property",
            _ => "Sub",
        };

        public ExtractionEdit Write()
        {
            var selection = plan.Selection;
            var start = text.OnlyIndentationBefore(selection.Range.Start) ? new SourcePosition(selection.Range.Start.Line, 0) : selection.Range.Start;
            var moved = new SourceRange(start, selection.Range.End);
            var indentation = text.IndentationOf(selection.Range.Start.Line);

            var resultCopy = plan.AssignsResult || plan.ReadsResult ? ResultCopyName() : null;
            var exits = ExitCodes();
            var edits = new List<ExtractionTextEdit>();

            // the declarations that move into the new procedure leave the source.
            var movedIn = plan.Locals.Where(local => local.MovesDeclaration).Select(local => local.Variable).ToHashSet();
            edits.AddRange(DeclarationEdits(movedIn, outsideTheSelection: true, ExtractionTextEditKind.Declaration));

            var body = Body(moved, start.Character == 0 ? string.Empty : indentation, resultCopy, exits);
            var procedure = ProcedureText(body, resultCopy, exits);
            var call = CallText(indentation, resultCopy, exits, fromLineStart: start.Character == 0, followed: text.StatementFollows(selection.Range.End));
            if (selection.Routine is { } routine)
            {
                // the routine leaves the source procedure, but for the declarations in it that stay there; each GoSub of it is a call, and each On…GoSub a
                // Select Case with the call.
                edits.Add(RoutineRemoval(moved, indentation));
                foreach (var site in routine.Sites.Select(site => site.SourceLocation.Range))
                {
                    call = CallText(text.IndentationOf(site.Start.Line), resultCopy, exits, fromLineStart: false, followed: text.StatementFollows(site.End));
                    edits.Add(new ExtractionTextEdit(site, call, ExtractionTextEditKind.Site));
                }

                foreach (var table in routine.Tables)
                {
                    edits.Add(TableText(routine, table, resultCopy, exits));
                }
            }
            else
            {
                edits.Add(new ExtractionTextEdit(moved, call, ExtractionTextEditKind.Call));
            }

            var after = source.Procedure.SourceLocation.Range.End;
            edits.Add(new ExtractionTextEdit(new SourceRange(after, after), $"{_newLine}{_newLine}{procedure}", ExtractionTextEditKind.Procedure));

            string? enumeration = null;
            if (exits.Count > 1)
            {
                enumeration = EnumerationText(exits);
                var first = source.Module.Children.OfType<MemberDeclarationNode>()
                    .Where(member => member.MemberKind is MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet)
                    .Min(member => member.SourceLocation.Range.Start);
                var at = new SourcePosition(first.Line, 0);
                edits.Add(new ExtractionTextEdit(new SourceRange(at, at), $"{enumeration}{_newLine}{_newLine}", ExtractionTextEditKind.Enumeration));
            }

            return new ExtractionEdit([.. edits.OrderBy(edit => edit.Range.Start)], procedure, call, enumeration, null);
        }

        // ---- the new procedure ----

        private string ProcedureText(IReadOnlyList<string> body, string? resultCopy, IReadOnlyList<Exit> exits)
        {
            var parameters = Given(optional: false)
                .Select(ParameterText)
                .Concat(plan.WithTarget is { } target ? [WithTargetText(target)] : [])
                .Concat(resultCopy is { } copy ? [$"ByRef {copy} As {ResultType()}"] : [])
                .Concat(Given(optional: true).Select(ParameterText))
                .ToArray();

            var returns = exits.Count switch
            {
                1 => " As Boolean",
                > 1 => $" As {_name}Exit",
                _ => ReturnValue is { } value ? $" As {ParameterType(value)}" : string.Empty,
            };
            var keyword = returns.Length > 0 ? "Function" : "Sub";

            var lines = new List<string> { $"{choices.Accessibility} {keyword} {_name}({string.Join(", ", parameters)}){returns}" };
            lines.AddRange(Declarations().Select(declaration => Indent + declaration));
            if (plan.StartsWithResumeNext)
            {
                lines.Add($"{Indent}On Error Resume Next");
            }

            if (plan.WithTarget is { } with)
            {
                lines.Add($"{Indent}With {with.Name}");
                lines.AddRange(body.Select(line => line.Length == 0 ? line : Indent + Indent + line));
                lines.Add($"{Indent}End With");
            }
            else
            {
                lines.AddRange(body.Select(line => line.Length == 0 ? line : Indent + line));
            }

            // the labels an On…GoTo goes to in place of its labels outside: after the body, which never runs on into them.
            var stubs = Stubs(exits);
            for (var index = 0; index < stubs.Count; index++)
            {
                if (index == 0)
                {
                    lines.Add($"{Indent}Exit Function");
                }

                lines.Add($"{stubs[index].Label}:");
                lines.Add($"{Indent}{_name} = {stubs[index].Exit.Code}");
                if (index < stubs.Count - 1)
                {
                    lines.Add($"{Indent}Exit Function");
                }
            }

            if (ReturnValue is { } returned)
            {
                lines.Add($"{Indent}{(IsObject(returned) ? "Set " : string.Empty)}{_name} = {LocalName(returned)}");
            }

            lines.Add($"End {keyword}");
            return string.Join(_newLine, lines);
        }

        // the constants the selection reads, and the locals the new procedure has of its own that the selection does not declare.
        private IEnumerable<string> Declarations()
        {
            foreach (var constant in plan.Constants.Select(ConstantNamed).OfType<ConstantDeclarationNode>())
            {
                yield return $"Const {DeclaratorOf(constant)}";
            }

            var own = plan.Locals.Select(local => (local.Variable, local.IsUndeclared))
                .Concat(ReturnValue is { } value ? [(value, plan.Parameters.Single(parameter => parameter.Variable == value).IsUndeclared)] : []);
            foreach (var (variable, isUndeclared) in own)
            {
                if (isUndeclared || DeclarationOf(variable) is not { } declaration)
                {
                    yield return $"Dim {LocalName(variable)} As {ParameterType(variable)}";
                }
                else if (!InTheSelection(declaration.SourceLocation.Range) || plan.MovedOut.Contains(variable))
                {
                    yield return $"Dim {DeclaratorOf(declaration, LocalName(variable))}";
                }
            }
        }

        // the selection, with what changes in it: the declarations that stay in the source, the source's result, and the exits; dedented. A run that starts
        // after another statement of its line is dedented as the line is.
        private List<string> Body(SourceRange moved, string lineIndentation, string? resultCopy, IReadOnlyList<Exit> exits)
        {
            var stays = plan.MovedOut.Where(variable => plan.Parameters.Any(parameter => parameter.Variable == variable) || variable == ReturnValue).ToHashSet();
            var edits = DeclarationEdits(stays, outsideTheSelection: false, ExtractionTextEditKind.Call).ToList();
            if (resultCopy is { } copy)
            {
                edits.AddRange(plan.ResultNames.Select(range => new ExtractionTextEdit(range, copy, ExtractionTextEditKind.Call)));
            }

            // a statement that leaves returns its exit code; an On…GoTo stays, its selector evaluated as before, and goes to the label that returns it.
            var jumpTables = JumpTables();
            var stubs = Stubs(exits).ToDictionary(stub => stub.Exit, stub => stub.Label);
            foreach (var exit in exits)
            {
                foreach (var from in exit.From)
                {
                    if (jumpTables.TryGetValue(from.Range, out var jumpTable))
                    {
                        edits.AddRange(jumpTable.Labels
                            .Where(label => text.Of(label.SourceLocation.Range).Trim().Equals(exit.Label, StringComparison.OrdinalIgnoreCase))
                            .Select(label => new ExtractionTextEdit(label.SourceLocation.Range, stubs[exit], ExtractionTextEditKind.Call)));
                    }
                    else
                    {
                        edits.Add(new ExtractionTextEdit(from.Range, $"{_name} = {exit.Code}: Exit Function", ExtractionTextEditKind.Call));
                    }
                }
            }

            var routine = plan.Selection.Routine;
            var leaves = $"Exit {(exits.Count > 0 ? "Function" : "Sub")}";
            if (routine is not null)
            {
                edits.AddRange(RoutineEdits(routine, leaves));
            }

            // the names of a variable the user renamed, but in a declaration that stays in the source, which leaves the body.
            var removed = edits.Select(edit => edit.Range).ToArray();
            edits.AddRange(choices.Parameters
                .SelectMany(choice => choice.Names.Select(range => new ExtractionTextEdit(range, choice.Name, ExtractionTextEditKind.Call)))
                .Where(rename => !removed.Any(range => range.Start <= rename.Range.Start && rename.Range.End <= range.End)));

            var lines = text.Of(moved, edits).Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
            lines[0] = lineIndentation + lines[0];
            var dedent = lines.Where(line => line.Trim().Length > 0).Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty(0).Min();
            List<string> body = [.. lines.Select(line => line.Trim().Length == 0 ? string.Empty : line[Math.Min(dedent, line.Length - line.TrimStart().Length)..])];
            if (routine is not null)
            {
                // the line of a label that goes, and the last Return, whose work the end of the procedure does.
                body = [.. body.SkipWhile(line => line.Length == 0)];
                if (body.Count > 0 && body[^1].Equals(leaves, StringComparison.Ordinal))
                {
                    body.RemoveAt(body.Count - 1);
                }
            }

            return body;
        }

        // the routine as the new procedure's body: its label goes, unless a jump in it names it, and each Return leaves the procedure.
        private IEnumerable<ExtractionTextEdit> RoutineEdits(GoSubRoutine routine, string leaves)
        {
            if (!routine.IsNamedInside)
            {
                var label = routine.Declaration.SourceLocation.Range;
                yield return new ExtractionTextEdit(new SourceRange(label.Start, text.AfterLabel(label.End)), string.Empty, ExtractionTextEditKind.Call);
            }

            foreach (var statement in routine.Returns)
            {
                yield return new ExtractionTextEdit(statement.SourceLocation.Range, leaves, ExtractionTextEditKind.Call);
            }
        }

        // an On…GoSub of the routine as a Select Case of its selector, as an Integer, with a case of each of its labels (MS-VBAL §5.4.2.16): the routine's case calls
        // the procedure, and any other is the GoSub it was. A selector of 0 or past the last label runs none; a negative one raises run-time error 5, as the
        // On…GoSub does.
        private ExtractionTextEdit TableText(GoSubRoutine routine, OnGoSubStatementNode table, string? resultCopy, IReadOnlyList<Exit> exits)
        {
            var range = table.SourceLocation.Range;
            var indentation = text.IndentationOf(range.Start.Line);
            var inner = indentation + Indent + Indent;
            var call = CallText(inner, resultCopy, exits, fromLineStart: false, followed: false);
            var lines = new List<string> { $"Select Case CInt({text.Of(table.Selector.SourceLocation.Range).Trim()})" };
            var callStart = -1;
            var labels = table.Labels.Select((label, index) => (Name: text.Of(label.SourceLocation.Range).Trim(), Case: index + 1));
            foreach (var target in labels.GroupBy(label => label.Name, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"{indentation}{Indent}Case {string.Join(", ", target.Select(label => label.Case))}");
                if (target.Key.Equals(routine.Label, StringComparison.OrdinalIgnoreCase))
                {
                    callStart = lines.Sum(line => line.Length + _newLine.Length) + inner.Length;
                    lines.Add(inner + call);
                }
                else
                {
                    lines.Add($"{inner}GoSub {target.Key}");
                }
            }

            // CInt is a reserved name (MS-VBAL §3.3.5.2), which nothing can declare; Err is not, and is the standard library's where another Err, the
            // workspace's or a referenced library's, would be meant.
            var err = source.Scopes.ScopeFor(source.ProcedureId.Uri).SelfAndAncestors().SelectMany(scope => scope.DeclaredAs("Err")).FirstOrDefault() is { } meant
                && !StandardLibrary.IsLibraryMember(meant)
                ? $"{StandardLibrary.LibraryName}.Err"
                : "Err";
            lines.Add($"{indentation}{Indent}Case Is < 0");
            lines.Add($"{inner}{err}.Raise 5");
            lines.Add($"{indentation}End Select");
            return new ExtractionTextEdit(range, string.Join(_newLine, lines), ExtractionTextEditKind.Site, callStart, call.Length);
        }

        // the routine's lines leave the source procedure: the declarations in it that stay there are written where it was.
        private ExtractionTextEdit RoutineRemoval(SourceRange moved, string indentation)
        {
            var stays = StayingDeclarations().ToArray();
            if (moved.Start.Character == 0 && text.OnlySpaceAfter(moved.End))
            {
                return new ExtractionTextEdit(new SourceRange(moved.Start, text.StartOfNextLine(moved.End)), string.Concat(stays.Select(line => indentation + line + _newLine)),
                    ExtractionTextEditKind.Call);
            }

            return new ExtractionTextEdit(moved, string.Join(_newLine + indentation, stays), ExtractionTextEditKind.Call);
        }

        // the parameters the new procedure is given, in the order it declares them: the required ones, or the Optional ones, which come after every other.
        private IEnumerable<ExtractionParameter> Given(bool optional)
            => _ordered.Where(parameter => parameter.Variable != ReturnValue && parameter.IsOptional == optional);

        public string ParameterText(ExtractionParameter parameter)
        {
            var passing = ExtractionParameters.ValueOf(ChoiceOf(parameter.Variable)?.Passing ?? parameter.Passing);
            var (name, hasHint) = WrittenName(parameter.Variable);
            if (parameter.Reason is ExtractionPassingReason.ParamArray)
            {
                return $"ByVal {LocalName(parameter.Variable)} As Variant";
            }

            if (parameter.IsOptional)
            {
                return $"Optional {passing} {name} As Variant";
            }

            var array = SymbolOf(parameter.Variable) is ITypedSymbol { ResolvedType: VBArrayType } ? "()" : string.Empty;
            return hasHint ? $"{passing} {name}{array}" : $"{passing} {name}{array} As {ParameterType(parameter.Variable)}";
        }

        private string WithTargetText(ExtractionWithTarget target)
            => $"{(target.Passing is ExtractionPassing.ByRef ? "ByRef" : "ByVal")} {target.Name} As {ParameterType(target.Variable)}";

        // ---- the call site ----

        // the declarations in the selection that stay in the source, as they were: those of one statement in it again.
        private IEnumerable<string> StayingDeclarations()
        {
            var movedOut = plan.MovedOut.Select(id => SymbolOf(id)?.Name).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var statement in Statements(_variables).Where(statement => InTheSelection(statement[0].SourceLocation.Range)))
            {
                var stays = statement.Where(node => movedOut.Contains(node.Name)).Select(DeclaratorOf).ToArray();
                if (stays.Length > 0)
                {
                    yield return $"{KeywordOf(statement[0]).TrimEnd()} {string.Join(", ", stays)}";
                }
            }
        }

        // followed: another statement of the line comes after the call, which a single-line If would take into its Then.
        private string CallText(string indentation, string? resultCopy, IReadOnlyList<Exit> exits, bool fromLineStart, bool followed)
        {
            var arguments = Given(optional: false).Select(parameter => parameter.Name)
                .Concat(plan.WithTarget is { } target ? [target.Name] : [])
                .Concat(resultCopy is { } copy ? [copy] : [])
                .Concat(Given(optional: true).Select(parameter => parameter.Name))
                .ToArray();
            // the declarations that stay in the source come before the call, as they were; those of a routine, where it was.
            var lines = plan.Selection.Routine is null ? StayingDeclarations().ToList() : [];

            var result = source.Procedure.Name;
            if (resultCopy is { } copied)
            {
                lines.Add($"Dim {copied} As {ResultType()}");
                lines.Add($"{(ResultIsObject() ? "Set " : string.Empty)}{copied} = {result}");
            }

            var invoked = $"{_name}({string.Join(", ", arguments)})";
            switch (exits.Count)
            {
                case 0 when ReturnValue is { } value:
                    lines.Add($"{(IsObject(value) ? "Set " : string.Empty)}{NameOf(value)} = {invoked}");
                    break;
                case 0:
                    lines.Add(arguments.Length == 0 ? _name : $"{_name} {string.Join(", ", arguments)}");
                    break;
                case 1 when followed:
                    lines.Add($"If {invoked} Then");
                    lines.Add($"{Indent}{exits[0].Dispatch}");
                    lines.Add("End If");
                    break;
                case 1:
                    lines.Add($"If {invoked} Then {exits[0].Dispatch}");
                    break;
                default:
                    lines.Add($"Select Case {invoked}");
                    foreach (var exit in exits)
                    {
                        lines.Add($"{Indent}Case {exit.Code}");
                        lines.Add($"{Indent}{Indent}{exit.Dispatch}");
                    }

                    lines.Add("End Select");
                    break;
            }

            if (resultCopy is { } back)
            {
                lines.Add($"{(ResultIsObject() ? "Set " : string.Empty)}{result} = {back}");
            }

            return string.Join(_newLine, lines.Select((line, index) => index == 0 && !fromLineStart ? line : indentation + line));
        }

        // ---- exits ----

        public sealed record class Exit(string Code, string Dispatch, ImmutableArray<SourceLocation> From, string? Label);

        // the On…GoTo statements of the selection, by where they are written.
        private Dictionary<SourceRange, OnGoToStatementNode> JumpTables() => plan.Selection.Statements
            .SelectMany(statement => (IEnumerable<SyntaxNode>)[statement, .. statement.Descendants()])
            .OfType<OnGoToStatementNode>()
            .ToDictionary(statement => statement.SourceLocation.Range);

        // the labels of the new procedure that an On…GoTo goes to for a label outside the selection: each returns the exit code of that label. They are named
        // for it, and none is named as a label that moves with the selection.
        private List<(Exit Exit, string Label)> Stubs(IReadOnlyList<Exit> exits)
        {
            var jumpTables = JumpTables();
            var taken = plan.Selection.Labels.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var stubs = new List<(Exit, string)>();
            foreach (var exit in exits.Where(exit => exit.From.Any(from => jumpTables.ContainsKey(from.Range))))
            {
                var label = $"{_name}To{exit.Label}";
                while (!taken.Add(label))
                {
                    label += "_";
                }

                stubs.Add((exit, label));
            }

            return stubs;
        }

        public List<Exit> ExitCodes()
        {
            var exits = new List<Exit>();
            var single = plan.Exits.Length == 1;
            foreach (var exit in plan.Exits)
            {
                var dispatch = exit.Kind switch
                {
                    ExtractionExitKind.Procedure => $"Exit {ProcedureKeyword}",
                    ExtractionExitKind.Loop => text.Of(exit.From[0].Range).Trim(),
                    _ => $"GoTo {exit.Label}",
                };
                var member = $"{_name}{string.Concat(dispatch.Split(' ', StringSplitOptions.RemoveEmptyEntries))}";
                while (exits.Any(other => other.Code == member))
                {
                    member += "_";
                }

                exits.Add(new Exit(single ? "True" : member, dispatch, exit.From, exit.Label));
            }

            return exits;
        }

        private string EnumerationText(IReadOnlyList<Exit> exits)
            => string.Join(_newLine, [$"{choices.Accessibility} Enum {_name}Exit", $"{Indent}{_name}ExitNone", .. exits.Select(exit => Indent + exit.Code), "End Enum"]);

        // ---- declarations ----

        // the declaration statements the variables are declared in, written again without them: a `Dim a, b` keeps the others, on as many lines as it was.
        private IEnumerable<ExtractionTextEdit> DeclarationEdits(IReadOnlySet<SemanticId> removed, bool outsideTheSelection, ExtractionTextEditKind kind)
        {
            var names = removed.Select(id => SymbolOf(id)?.Name).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var statement in Statements(_variables))
            {
                var range = new SourceRange(statement[0].SourceLocation.Range.Start, statement[^1].SourceLocation.Range.End);
                if (InTheSelection(range) == outsideTheSelection || !statement.Any(node => names.Contains(node.Name)))
                {
                    continue;
                }

                var kept = statement.Where(node => !names.Contains(node.Name)).Select(DeclaratorOf).ToArray();
                if (kept.Length > 0)
                {
                    yield return new ExtractionTextEdit(range, $"{KeywordOf(statement[0])}{string.Join(", ", kept)}", kind);
                }
                else if (text.OnlyIndentationBefore(range.Start) && text.OnlySpaceAfter(range.End))
                {
                    // the line holds the statement alone, and goes with it.
                    yield return new ExtractionTextEdit(new SourceRange(new SourcePosition(range.Start.Line, 0), text.StartOfNextLine(range.End)), string.Empty, kind);
                }
                else
                {
                    yield return new ExtractionTextEdit(range, string.Empty, kind);
                }
            }
        }

        // the declarations grouped by the statements they are written in: the first of each is written after its keyword, the others after a comma.
        private List<List<VariableDeclarationNode>> Statements(IEnumerable<VariableDeclarationNode> declarations)
        {
            var statements = new List<List<VariableDeclarationNode>>();
            foreach (var declaration in declarations.OrderBy(node => node.SourceLocation.Range.Start))
            {
                if (KeywordOf(declaration).Length > 0 || statements.Count == 0)
                {
                    statements.Add([]);
                }

                statements[^1].Add(declaration);
            }

            return statements;
        }

        private string KeywordOf(SyntaxNode declaration) => NameRangeOf(declaration) is { } name
            ? text.Of(new SourceRange(declaration.SourceLocation.Range.Start, name.Start))
            : string.Empty;

        // what declares the one variable or constant: its name, its bounds, its type, its value; not the keyword of the statement.
        private string DeclaratorOf(SyntaxNode declaration)
            => text.Of(new SourceRange(NameRangeOf(declaration)?.Start ?? declaration.SourceLocation.Range.Start, declaration.SourceLocation.Range.End)).Trim();

        // the declarator, with the variable named as the new procedure names it: its type-declaration character, when it has one, stays.
        private string DeclaratorOf(SyntaxNode declaration, string name)
        {
            if (NameRangeOf(declaration) is not { } range)
            {
                return DeclaratorOf(declaration);
            }

            var written = text.Of(range);
            return SymbolName(written).Equals(name, StringComparison.Ordinal)
                ? DeclaratorOf(declaration)
                : $"{name}{written[SymbolName(written).Length..]}{text.Of(new SourceRange(range.End, declaration.SourceLocation.Range.End))}".Trim();
        }

        // a written name without its type-declaration character.
        private static string SymbolName(string written) => written.Length > 0 && "$%&!#@^".Contains(written[^1]) ? written[..^1] : written;

        // the name the new procedure gives a variable: the one the user chose for its parameter, or its own.
        private string LocalName(SemanticId variable) => ChoiceOf(variable)?.Name ?? NameOf(variable);

        private static SourceRange? NameRangeOf(SyntaxNode declaration) => declaration switch
        {
            VariableDeclarationNode variable => variable.NameRange,
            ConstantDeclarationNode constant => constant.NameRange,
            ParameterDeclarationNode parameter => parameter.NameRange,
            _ => null,
        };

        private bool InTheSelection(SourceRange range) => range.Start >= plan.Selection.Range.Start && range.End <= plan.Selection.Range.End;

        private VariableDeclarationNode? DeclarationOf(SemanticId variable)
            => SymbolOf(variable) is { } symbol ? _variables.FirstOrDefault(node => node.Name.Equals(symbol.Name, StringComparison.OrdinalIgnoreCase)) : null;

        private ConstantDeclarationNode? ConstantNamed(SemanticId constant)
            => SymbolOf(constant) is { } symbol ? _constants.FirstOrDefault(node => node.Name.Equals(symbol.Name, StringComparison.OrdinalIgnoreCase)) : null;

        private Symbol? SymbolOf(SemanticId id) => ExtractionAnalysis.SymbolOf(source.Scopes, id);

        private string NameOf(SemanticId variable) => SymbolOf(variable)?.Name ?? string.Empty;

        // the name as it is declared, with its type-declaration character when it has one: then it has no As clause.
        private (string Name, bool HasHint) WrittenName(SemanticId variable)
        {
            var declaration = (SyntaxNode?)DeclarationOf(variable)
                ?? _parameters.FirstOrDefault(node => node.Name.Equals(NameOf(variable), StringComparison.OrdinalIgnoreCase));
            if (declaration is not null && NameRangeOf(declaration) is { } range)
            {
                var written = text.Of(range);
                var symbolName = SymbolName(written);
                var name = ChoiceOf(variable)?.Name is { } chosen ? $"{chosen}{written[symbolName.Length..]}" : written;
                return (name, symbolName.Length < written.Length);
            }

            return (LocalName(variable), false);
        }

        public string DeclaredType(SemanticId variable)
        {
            var declaration = (SyntaxNode?)DeclarationOf(variable)
                ?? (SyntaxNode?)ConstantNamed(variable)
                ?? _parameters.FirstOrDefault(node => node.Name.Equals(NameOf(variable), StringComparison.OrdinalIgnoreCase));
            if (declaration is not null && NameRangeOf(declaration) is { } range)
            {
                var written = text.Of(range);
                return written[SymbolName(written).Length..] + text.Of(new SourceRange(range.End, declaration.SourceLocation.Range.End));
            }

            return SymbolOf(variable) is ITypedSymbol { ResolvedType: { } type } ? type.Name : "Variant";
        }

        // the type a variable is declared as, as a parameter writes it: no New, and a String for a fixed-length one; or the type its DefType gives it.
        private string ParameterType(SemanticId variable)
        {
            var declaration = (SyntaxNode?)DeclarationOf(variable)
                ?? _parameters.FirstOrDefault(node => node.Name.Equals(NameOf(variable), StringComparison.OrdinalIgnoreCase));
            if (declaration is not null && AsClauseOf(declaration) is { } written)
            {
                return written;
            }

            return SymbolOf(variable) is ITypedSymbol { ResolvedType: { } type } && type is not VBUnknownType ? NameOfType(type) : "Variant";
        }

        private string? AsClauseOf(SyntaxNode declaration)
        {
            var after = text.Of(new SourceRange(NameRangeOf(declaration)?.End ?? declaration.SourceLocation.Range.Start, declaration.SourceLocation.Range.End));
            var at = after.IndexOf(" As ", StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return null;
            }

            var type = after[(at + 4)..].Trim();
            var assigned = type.IndexOf('=');
            type = (assigned >= 0 ? type[..assigned] : type).Trim();
            if (type.StartsWith("New ", StringComparison.OrdinalIgnoreCase))
            {
                type = type[4..].Trim();
            }

            return type.Contains('*') ? "String" : type;
        }

        private static string NameOfType(VBType type) => type switch
        {
            VBArrayType array when array.ItemType is { } element and not VBArrayType => NameOfType(element),
            VBFixedStringType => "String",
            _ => type.Name,
        };

        private bool IsObject(SemanticId variable) => SymbolOf(variable) is ITypedSymbol { ResolvedType: VBClassType or VBObjectType };

        // ---- the source's result ----

        public string ResultCopyName()
        {
            var taken = _variables.Select(node => node.Name).Concat(_parameters.Select(node => node.Name)).Concat(_constants.Select(node => node.Name)).Append(_name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = $"{source.Procedure.Name}Result";
            for (var index = 2; taken.Contains(name); index++)
            {
                name = $"{source.Procedure.Name}Result{index}";
            }

            return name;
        }

        private string ResultType() => SymbolOf(source.ProcedureId) is ITypedSymbol { ResolvedType: { } type } && type is not VBUnknownType ? NameOfType(type) : "Variant";

        private bool ResultIsObject() => SymbolOf(source.ProcedureId) is ITypedSymbol { ResolvedType: VBClassType or VBObjectType };
    }
}
