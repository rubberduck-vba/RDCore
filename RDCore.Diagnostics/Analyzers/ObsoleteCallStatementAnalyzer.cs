using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Extensibility.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00302</c>: a <c>Call</c> statement written with the <c>Call</c> keyword, which is obsolete.
/// </summary>
/// <remarks>
/// <para>
/// The call is the same without the keyword. Whether a callee is the callee of a statement written with it is a fact of the expression
/// (<see cref="ValueExpressionSemanticFlags.ExplicitCallKeyword"/>), which is what this reads.
/// </para>
/// <para>
/// The fix takes the keyword out, and the parentheses around the arguments with it: <c>Call Foo(1, 2)</c> is written <c>Foo 1, 2</c>. The tree does not
/// say where the parentheses are, so it is offered only where they can be nothing else: the <c>(</c> right after the name and the <c>)</c> right after the
/// last argument, on the same line. A callee that is itself indexed, such as <c>Call Foo(1)(2)</c>, and the <c>Mid</c> functions, whose name without
/// <c>Call</c> starts the <c>Mid</c> statement, are left as they are.
/// </para>
/// </remarks>
internal sealed class ObsoleteCallStatementAnalyzer : IModuleAnalyzer
{
    private static readonly HashSet<string> StatementNames = new(["Mid", "Mid$", "MidB", "MidB$"], StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.ObsoleteCallStatement.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        var statements = context.ParseResult.SyntaxTree is { } module
            ? module.Descendants().OfType<CallStatementNode>().Where(statement => statement.IsExplicitCall).ToList()
            : [];

        return (context.Semantics?.Procedures ?? [])
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.ExplicitCallKeyword))
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.ObsoleteCallStatement.ToCode(), fact.Location.Range, DiagnosticSeverity.Hint, RDCoreDiagnosticsResources.ObsoleteCallStatement_Message)
            {
                Fixes = statements.FirstOrDefault(statement => statement.SourceLocation.Range.Contains(fact.Location.Range.Start)) is { } call
                    ? FixOf(call)
                    : null,
            });
    }

    private static DiagnosticFixes? FixOf(CallStatementNode statement)
    {
        var (callee, arguments, end) = statement.Arguments.IsDefaultOrEmpty && statement.Callee is IndexExpressionNode index
            ? (index.Callee, index.Arguments, index.SourceLocation.Range.End)
            : (statement.Callee, statement.Arguments, statement.SourceLocation.Range.End);
        if (callee is IndexExpressionNode || callee is SimpleNameExpressionNode name && StatementNames.Contains(name.IdentifierName + name.TypeHint))
        {
            return null;
        }

        var edits = new List<TextEdit> { SyntaxEdits.Remove(new SourceRange(statement.SourceLocation.Range.Start, callee.SourceLocation.Range.Start)) };
        var nameEnd = callee.SourceLocation.Range.End;
        // empty parentheses are an argument list of one missing argument.
        if (arguments.IsDefaultOrEmpty || arguments is [MissingArgumentNode only] && only.SourceLocation.Range.Start == only.SourceLocation.Range.End)
        {
            if (SyntaxEdits.Width(nameEnd, end) is 2)
            {
                edits.Add(SyntaxEdits.Remove(new SourceRange(nameEnd, end)));
            }
            else if (SyntaxEdits.Width(nameEnd, end) is not 0)
            {
                return null;
            }
        }
        else
        {
            var first = arguments[0].SourceLocation.Range;
            var last = arguments[^1].SourceLocation.Range;
            if (SyntaxEdits.Width(nameEnd, first.Start) is not 1 || SyntaxEdits.Width(last.End, end) is not 1 || first.Start == first.End || last.Start == last.End)
            {
                return null;
            }

            edits.Add(SyntaxEdits.Replace(new SourceRange(nameEnd, first.Start), " "));
            edits.Add(SyntaxEdits.Remove(new SourceRange(last.End, end)));
        }

        return new DiagnosticFixes { Edits = [SyntaxEdits.Fix(RDCoreDiagnosticsResources.ObsoleteCallStatement_Fix, [.. edits])] };
    }
}
