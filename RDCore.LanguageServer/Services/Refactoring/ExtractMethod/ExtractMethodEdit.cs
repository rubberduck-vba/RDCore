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
    /// The <c>Private Enum</c> of the exit codes, before the module's first procedure.
    /// </summary>
    Enumeration,
}

/// <summary>
/// A change of the text of a module: what is written in a range, written instead.
/// </summary>
/// <param name="Range">What is replaced; an empty range inserts.</param>
/// <param name="NewText">What is written there.</param>
/// <param name="Kind">What it writes.</param>
internal readonly record struct ExtractionTextEdit(SourceRange Range, string NewText, ExtractionTextEditKind Kind);

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
    /// An <c>On…GoTo</c> leaves the selection: one statement with several targets, which no exit code of one statement can stand for.
    /// </summary>
    ExitByJumpTable,

    /// <summary>
    /// The selection leaves the source procedure, or a loop or a block around it, and the user turned exit routing off (<see cref="ExtractionChoices.RouteExits"/>).
    /// </summary>
    ExitRoutingDeclined,
}

/// <summary>
/// The extraction, written: the edits of the module, and the texts they are made of, which a preview shows.
/// </summary>
/// <param name="Edits">The edits of the module, in the order of the text, none of which overlaps another; empty when <see cref="Refusal"/> says why.</param>
/// <param name="Procedure">The new procedure, as it is inserted after the source procedure.</param>
/// <param name="Call">What replaces the selection: the declarations that stay in the source, and the call.</param>
/// <param name="Enumeration">The <c>Private Enum</c> of the exit codes, inserted before the module's first procedure; <see langword="null"/> when there is none.</param>
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
            // the line that follows an edit ending with a line break is not the edit's.
            foreach (var indent in indentation.Where(indent => indent.Start >= start && indent.Start < end && indent.End <= end).OrderByDescending(indent => indent.Start))
            {
                formatted = string.Concat(formatted.AsSpan(0, indent.Start - start), indent.NewText, formatted.AsSpan(indent.End - start));
            }

            return edit with { NewText = formatted };
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
/// Writes the extraction a plan says: the new procedure, <c>Private</c>, after the source procedure; the call that replaces the selection; the declarations that
/// move; and the routing of the exits.
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
/// procedure, or a loop or a block around it, returns a code: <c>True</c> for one exit, a member of a <c>Private Enum</c> for several. The call site goes where
/// the code says. The source's result, read or written in the selection, is given to the new procedure as a copy, <c>ByRef</c>, and written back after the call:
/// a function's name cannot be passed.
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

        var jumpTables = plan.Selection.Statements
            .SelectMany(statement => (IEnumerable<SyntaxNode>)[statement, .. statement.Descendants()])
            .OfType<OnGoToStatementNode>()
            .Select(statement => statement.SourceLocation.Range)
            .ToHashSet();
        if (plan.Exits.SelectMany(exit => exit.From).Any(from => jumpTables.Contains(from.Range)))
        {
            return ExtractionEdit.Refused(ExtractionEditRefusalKind.ExitByJumpTable);
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

            edits.Add(new ExtractionTextEdit(moved, call, ExtractionTextEditKind.Call));
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

            var lines = new List<string> { $"Private {keyword} {_name}({string.Join(", ", parameters)}){returns}" };
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

            foreach (var exit in exits)
            {
                edits.AddRange(exit.From.Select(from => new ExtractionTextEdit(from.Range, $"{_name} = {exit.Code}: Exit Function", ExtractionTextEditKind.Call)));
            }

            // the names of a variable the user renamed, but in a declaration that stays in the source, which leaves the body.
            var removed = edits.Select(edit => edit.Range).ToArray();
            edits.AddRange(choices.Parameters
                .SelectMany(choice => choice.Names.Select(range => new ExtractionTextEdit(range, choice.Name, ExtractionTextEditKind.Call)))
                .Where(rename => !removed.Any(range => range.Start <= rename.Range.Start && rename.Range.End <= range.End)));

            var lines = text.Of(moved, edits).Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
            lines[0] = lineIndentation + lines[0];
            var dedent = lines.Where(line => line.Trim().Length > 0).Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty(0).Min();
            return [.. lines.Select(line => line.Trim().Length == 0 ? string.Empty : line[Math.Min(dedent, line.Length - line.TrimStart().Length)..])];
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

        // followed: another statement of the line comes after the call, which a single-line If would take into its Then.
        private string CallText(string indentation, string? resultCopy, IReadOnlyList<Exit> exits, bool fromLineStart, bool followed)
        {
            var arguments = Given(optional: false).Select(parameter => parameter.Name)
                .Concat(plan.WithTarget is { } target ? [target.Name] : [])
                .Concat(resultCopy is { } copy ? [copy] : [])
                .Concat(Given(optional: true).Select(parameter => parameter.Name))
                .ToArray();
            var lines = new List<string>();

            // the declarations that stay in the source come before the call, as they were: those of one statement in it again.
            var movedOut = plan.MovedOut.Select(id => SymbolOf(id)?.Name).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var statement in Statements(_variables).Where(statement => InTheSelection(statement[0].SourceLocation.Range)))
            {
                var stays = statement.Where(node => movedOut.Contains(node.Name)).Select(DeclaratorOf).ToArray();
                if (stays.Length > 0)
                {
                    lines.Add($"{KeywordOf(statement[0]).TrimEnd()} {string.Join(", ", stays)}");
                }
            }

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

        public sealed record class Exit(string Code, string Dispatch, ImmutableArray<SourceLocation> From);

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

                exits.Add(new Exit(single ? "True" : member, dispatch, exit.From));
            }

            return exits;
        }

        private string EnumerationText(IReadOnlyList<Exit> exits)
            => string.Join(_newLine, [$"Private Enum {_name}Exit", $"{Indent}{_name}ExitNone", .. exits.Select(exit => Indent + exit.Code), "End Enum"]);

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
