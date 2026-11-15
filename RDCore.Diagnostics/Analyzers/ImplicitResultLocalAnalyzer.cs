using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00115</c>: a function or a property getter that reads its own return value - the function result variable (<strong>MS-VBAL §5.3.1</strong>) - as the
/// local variable it implicitly is.
/// </summary>
/// <remarks>
/// <para>
/// <c>Total = Total + Item</c> in the body of <c>Total</c> works: the name of the procedure is a variable of its body, which holds what it returns. It reads as a
/// call of the procedure, though, which it is anywhere else, and which <c>Total(1)</c> is in a function that takes an argument. A local declared for the work,
/// assigned to the return value once at the end, says what the code does. Each read is reported where it is: the host binds the name to the procedure and does
/// not flag it as what is assigned (<see cref="ValueExpressionSemanticFlags.AssignmentTarget"/>), and the syntax tree says it is not the procedure called.
/// </para>
/// <para>
/// The fix (<see cref="DiagnosticFixes.Edits"/>) is made of the procedure's own syntax tree: a <c>Dim</c> of a local before the first statement, of the
/// procedure's return type; the local in place of each name of the result variable in the body, read or assigned; and the local assigned to the return value
/// before each <c>Exit Function</c> (or <c>Exit Property</c>) - on its line, so that it is where the exit is, after a <c>Then</c> too - and before the end of the
/// procedure, after a label or a statement on the line of <c>End Function</c>, with <c>Set</c> when the body <c>Set</c>s its result. A result the body both
/// <c>Set</c>s and assigns a value is returned with <c>If IsObject(...)</c>. In a <c>Static</c> procedure the local is given its type's default value after
/// its <c>Dim</c>, as the result variable is at each call. It is not offered for a procedure whose body starts on the line of its declaration; for a
/// result both <c>Set</c> and assigned a value, when it has an <c>Exit</c> or a statement before <c>End Function</c>; nor, in a <c>Static</c> procedure, for a
/// type whose default value has no literal.
/// </para>
/// </remarks>
internal sealed class ImplicitResultLocalAnalyzer : IModuleAnalyzer
{
    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ImplicitResultLocal.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics)
        {
            yield break;
        }

        var facts = semantics.Procedures.SelectMany(procedure => procedure.Expressions).GroupBy(fact => fact.Node).ToDictionary(group => group.Key, group => group.First());
        foreach (var procedure in module.Descendants().OfType<MemberDeclarationNode>().Where(member => member.MemberKind is MemberKind.Function or MemberKind.PropertyGet))
        {
            var body = procedure.Descendants().ToArray();
            var called = body.OfType<IndexExpressionNode>().Select(call => call.Callee.Identity).ToHashSet();

            // every name of the result variable in the body, read or assigned.
            var names = body.OfType<SimpleNameExpressionNode>()
                .Where(name => string.Equals(name.IdentifierName, procedure.Name, StringComparison.OrdinalIgnoreCase) && !called.Contains(name.Identity))
                .Select(name => (Name: name, Fact: facts.GetValueOrDefault(name.Identity)))
                .Where(occurrence => occurrence.Fact?.Classification is ExpressionClassification.Function or ExpressionClassification.Property)
                .ToArray();
            var reads = names.Where(occurrence => !occurrence.Fact!.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget)).ToArray();
            if (reads.Length == 0)
            {
                continue;
            }

            var fixes = FixOf(context.ParseResult, procedure, body, names, semantics) is { } fix ? new DiagnosticFixes { Edits = [fix] } : null;
            foreach (var (name, _) in reads)
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.ImplicitResultLocal.ToCode(),
                    name.Location.Range,
                    DiagnosticSeverity.Information,
                    AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitResultLocal_Message, procedure.Name))
                {
                    Fixes = fixes,
                };
            }
        }
    }

    private static EditFix? FixOf(
        ModuleParseResult parse, MemberDeclarationNode procedure, SyntaxNode[] body, (SimpleNameExpressionNode Name, ExpressionFactDto? Fact)[] names, ModuleSemanticsDto semantics)
    {
        // the first node of the body, past the declaration's parameters and return type, which the Dim goes before at its indentation.
        var first = procedure.Children
            .Where(child => child is not (ParameterDeclarationNode or AsTypeExpressionNode))
            .OrderBy(child => child.SourceLocation.Range.Start)
            .FirstOrDefault();
        if (first is null || first.SourceLocation.Range.Start.Line <= procedure.SourceLocation.Range.Start.Line)
        {
            return null;
        }

        var indent = new string(' ', first.SourceLocation.Range.Start.Character);
        var local = LocalName(body, semantics);
        var type = ResultTypeOf(procedure, names);
        var identities = names.Select(occurrence => occurrence.Name.Identity).ToHashSet();
        var assignments = body.OfType<AssignmentStatementNode>()
            .Where(assignment => assignment.Target is SimpleNameExpressionNode target && identities.Contains(target.Identity))
            .ToArray();
        var assignsAnObject = assignments.Any(assignment => assignment.Kind is AssignmentKind.Set);
        var exits = body.OfType<KeywordStatementNode>().Where(statement => statement.Token is Tokens.ExitFunction or Tokens.ExitProperty).ToArray();

        // what is written before End Function on its line (a label, a statement), which the last return goes after so that a GoTo to the label returns it too.
        if (procedure.EndRange is not { } end)
        {
            return null;
        }

        var endIsAlone = !body.Any(node => node.SourceLocation.Range.Start.Line == end.Start.Line);

        // a result that is Set and also Let-assigned (a Variant that holds an object or a value) is returned as what it holds; the If that says which cannot share
        // a line with an Exit or with End Function, of which it would make a branch.
        var mixed = assignsAnObject && assignments.Any(assignment => assignment.Kind is not AssignmentKind.Set);
        if (mixed && (exits.Length > 0 || !endIsAlone))
        {
            return null;
        }

        var returns = mixed
            ? $"If IsObject({local}) Then Set {procedure.Name} = {local} Else {procedure.Name} = {local}"
            : $"{(assignsAnObject ? "Set " : string.Empty)}{procedure.Name} = {local}";

        // every local of a Static procedure keeps its value from one call to the next, and the result variable does not: each call starts with its default value
        // (MS-VBAL §5.3.1.6), which the local is then given first.
        List<string> declaration = [$"{indent}{Declaration(local, type)}"];
        if (procedure.IsStatic)
        {
            if (ResetOf(local, type) is not { } reset)
            {
                return null;
            }

            declaration.Add($"{indent}{reset}");
        }

        return SyntaxEdits.Fix(
            AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitResultLocal_Fix, procedure.Name),
            [
                LineEdits.InsertLinesBefore(parse, first, [.. declaration]),
                .. names.Select(occurrence => SyntaxEdits.Replace(occurrence.Name.Location.Range, local)),
                .. exits.Select(exit => SyntaxEdits.Insert(exit.SourceLocation.Range.Start, $"{returns}: ")),
                endIsAlone ? LineEdits.InsertLinesBefore(parse, end, $"{indent}{returns}") : SyntaxEdits.Insert(end.Start, $"{returns}: "),
            ]);
    }

    // the procedure's return type as it is written - an As clause, or a type character - and otherwise the type the host says the result has.
    private static (string Name, bool IsArray) ResultTypeOf(MemberDeclarationNode procedure, (SimpleNameExpressionNode Name, ExpressionFactDto? Fact)[] names)
    {
        if (procedure.Children.OfType<AsTypeExpressionNode>().FirstOrDefault() is { } asType)
        {
            return (asType.QualifierName is { Length: > 0 } qualifier ? $"{qualifier}.{asType.TypeName}" : asType.TypeName, asType.IsArrayDef);
        }

        var hinted = procedure.TypeHint is { } hint && IntrinsicVBTypes.TryResolveTypeHint(hint, out var hintType) ? hintType.Name : null;
        var declared = hinted ?? names.Select(occurrence => occurrence.Fact?.DeclaredType).FirstOrDefault(type => type is { Length: > 0 }) ?? VBTypeNames.VBVariant;
        return declared.EndsWith("()", StringComparison.Ordinal) ? (declared[..^2], true) : (declared, false);
    }

    private static string Declaration(string local, (string Name, bool IsArray) type)
        => $"Dim {local}{(type.IsArray ? "()" : string.Empty)} As {type.Name}";

    // the statement that gives the local the default value of the type; none for a type whose default only its declaration knows (a class, a user-defined type,
    // an Enum).
    private static string? ResetOf(string local, (string Name, bool IsArray) type)
        => type.IsArray
            ? $"Erase {local}"
            : IntrinsicVBTypes.TryResolve(type.Name, out var intrinsic) && IntrinsicVBTypes.DefaultLiteralOf(intrinsic) is { } value
                ? intrinsic is VBObjectType ? $"Set {local} = {value}" : $"{local} = {value}"
                : null;

    // "result", or the first of "result2", "result3"... that names nothing the module declares or the body refers to.
    private static string LocalName(SyntaxNode[] body, ModuleSemanticsDto semantics)
    {
        var taken = semantics.Declarations.Select(declaration => declaration.Name)
            .Concat(body.OfType<SimpleNameExpressionNode>().Select(name => name.IdentifierName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = "result";
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"result{n}";
        }

        return candidate;
    }
}
