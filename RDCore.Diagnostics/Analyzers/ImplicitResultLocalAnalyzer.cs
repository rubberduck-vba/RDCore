using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics.Flags;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

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
/// The fix (<see cref="DiagnosticFixes.Edit"/>) is made of the procedure's own syntax tree: a <c>Dim</c> of a local before the first statement, of the
/// procedure's return type; the local in place of each name of the result variable in the body, read or assigned; and the local assigned to the return value
/// before each <c>Exit Function</c> (or <c>Exit Property</c>) - on its line, so that it is where the exit is, after a <c>Then</c> too - and before the end of the
/// procedure, with <c>Set</c> when the body <c>Set</c>s its result. It is not offered for a procedure whose body starts on the line of its declaration.
/// </para>
/// </remarks>
internal sealed class ImplicitResultLocalAnalyzer : IModuleAnalyzer
{
    private const string NewLine = "\r\n";

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

            var fixes = FixOf(procedure, body, names, semantics) is { } fix ? new DiagnosticFixes { Edit = fix } : null;
            foreach (var (name, _) in reads)
            {
                yield return new AnalyzerFinding(
                    RDCoreDiagnosticId.ImplicitResultLocal,
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
        MemberDeclarationNode procedure, SyntaxNode[] body, (SimpleNameExpressionNode Name, ExpressionFactDto? Fact)[] names, ModuleSemanticsDto semantics)
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
        var identities = names.Select(occurrence => occurrence.Name.Identity).ToHashSet();
        var assignsAnObject = body.OfType<AssignmentStatementNode>()
            .Any(assignment => assignment.Kind is AssignmentKind.Set && assignment.Target is SimpleNameExpressionNode target && identities.Contains(target.Identity));
        var returns = $"{(assignsAnObject ? "Set " : string.Empty)}{procedure.Name} = {local}";

        var edits = new List<TextEdit>
        {
            Insert(first.SourceLocation.Range.Start.Line, 0, $"{indent}{Declaration(procedure, local, names)}{NewLine}"),
        };
        edits.AddRange(names.Select(occurrence => new TextEdit { Range = RangeOf(occurrence.Name.Location.Range), NewText = local }));
        edits.AddRange(body.OfType<KeywordStatementNode>()
            .Where(statement => statement.Token is Tokens.ExitFunction or Tokens.ExitProperty)
            .Select(exit => Insert(exit.SourceLocation.Range.Start.Line, exit.SourceLocation.Range.Start.Character, $"{returns}: ")));
        edits.Add(Insert(procedure.SourceLocation.Range.End.Line, 0, $"{indent}{returns}{NewLine}"));

        return new EditFix
        {
            Title = AnalyzerMessages.Format(RDCoreDiagnosticsResources.ImplicitResultLocal_Fix, procedure.Name),
            Edits = edits,
        };
    }

    // the Dim of the local: the procedure's return type as it is written - an As clause, or a type character - and otherwise the type the host says the result has.
    private static string Declaration(MemberDeclarationNode procedure, string local, (SimpleNameExpressionNode Name, ExpressionFactDto? Fact)[] names)
    {
        if (procedure.Children.OfType<AsTypeExpressionNode>().FirstOrDefault() is { } asType)
        {
            var typeName = asType.QualifierName is { Length: > 0 } qualifier ? $"{qualifier}.{asType.TypeName}" : asType.TypeName;
            return $"Dim {local}{(asType.IsArrayDef ? "()" : string.Empty)} As {typeName}";
        }

        var declared = TypeOfHint(procedure.TypeHint) ?? names.Select(occurrence => occurrence.Fact?.DeclaredType).FirstOrDefault(type => type is { Length: > 0 }) ?? "Variant";
        return declared.EndsWith("()", StringComparison.Ordinal) ? $"Dim {local}() As {declared[..^2]}" : $"Dim {local} As {declared}";
    }

    private static string? TypeOfHint(string? hint) => hint switch
    {
        "%" => "Integer",
        "&" => "Long",
        "^" => "LongLong",
        "!" => "Single",
        "#" => "Double",
        "@" => "Currency",
        "$" => "String",
        _ => null,
    };

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

    private static TextEdit Insert(int line, int character, string text)
        => new() { Range = new Range(line, character, line, character), NewText = text };

    private static Range RangeOf(SourceRange range)
        => new(range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);
}
