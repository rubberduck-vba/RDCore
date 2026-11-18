using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC01008</c>: a procedure a <c>Declare</c> statement declares (<strong>MS-VBAL §5.2.3.5</strong>) on every platform, and a call of one compiled on a
/// platform the procedure is not declared on, or on every platform.
/// </summary>
/// <remarks>
/// <para>
/// No library is on every platform: a <c>Declare</c> that no <c>#If</c> on the platform constants (<c>Win32</c>, <c>Win64</c>, <c>Mac</c>, <c>Linux</c>,
/// <c>Linux64</c>) makes dead calls its library on a platform that does not have it, where the call raises run-time error 53 or 453. A procedure written
/// once in each branch of a <c>#If</c> on them is declared on every platform, each by the statement of its own branch, and is not one. The host states where
/// each statement and each call is compiled, every platform considered and not only the one it runs on (<see cref="DeclarationFactDto.StatementsCompiledOn"/>,
/// <see cref="ExpressionFactDto.NativeCall"/>).
/// </para>
/// <para>
/// The fix writes the <c>Declare</c> statement in a <c>#If</c> on the constant of the platform its library is of: <c>Mac</c> for a <c>.dylib</c> or a
/// framework, <c>Linux</c> for a <c>.so</c>, and <c>Win32</c>, which is true on 64-bit Windows too, otherwise. It is not offered where the statement shares a
/// line with another.
/// </para>
/// </remarks>
internal sealed class NativeCallReachableOnEveryPlatformAnalyzer : IModuleAnalyzer
{
    private const string NewLine = "\r\n";

    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.NativeCallReachableOnEveryPlatform.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.Semantics is not { } semantics)
        {
            yield break;
        }

        var code = RDCoreDiagnosticId.NativeCallReachableOnEveryPlatform.ToCode();
        var module = context.ParseResult.SyntaxTree;
        // a procedure written once in each branch of a #If is declared on every platform by statements that are each on some: it is a statement compiled on
        // every platform that calls its one library whatever the platform.
        foreach (var declaration in semantics.Declarations)
        {
            if (declaration.StatementsCompiledOn is not { } compiled || !compiled.Contains(CompiledPlatforms.All))
            {
                continue;
            }

            var message = AnalyzerMessages.Format(RDCoreDiagnosticsResources.NativeCallReachableOnEveryPlatform_Message, declaration.Name);
            var statements = module?.Children.OfType<ExternalMemberDeclarationNode>()
                .Where(node => string.Equals(node.Name, declaration.Name, StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
            if (module is null || statements.Count != compiled.Length)
            {
                yield return new AnalyzerFinding(code, declaration.Location.Range, DiagnosticSeverity.Warning, message);
                continue;
            }

            foreach (var (statement, platforms) in statements.Zip(compiled))
            {
                if (platforms is CompiledPlatforms.All)
                {
                    yield return new AnalyzerFinding(code, statement.NameRange ?? statement.SourceLocation.Range, DiagnosticSeverity.Warning, message)
                    {
                        Fixes = FixOf(module, statement),
                    };
                }
            }
        }

        // a call is the name the procedure is called by: the expression around it is bound to the procedure as well, and is the same call.
        var names = module?.Descendants().OfType<ExpressionNode>().Where(node => node is SimpleNameExpressionNode or MemberAccessExpressionNode)
            .Select(node => node.Identity).ToHashSet() ?? [];
        foreach (var fact in semantics.Procedures.SelectMany(procedure => procedure.Expressions).Where(fact => names.Contains(fact.Node)))
        {
            if (fact is not { NativeCall: { } platforms, Binding: { } binding })
            {
                continue;
            }

            var message = platforms.Call is CompiledPlatforms.All && platforms.DeclaredByStatement.Contains(CompiledPlatforms.All)
                ? RDCoreDiagnosticsResources.NativeCallReachableOnEveryPlatform_CallEverywhere_Message
                : (platforms.Call & ~platforms.Declared) != CompiledPlatforms.None
                    ? RDCoreDiagnosticsResources.NativeCallReachableOnEveryPlatform_CallNotDeclared_Message
                    : null;
            if (message is not null)
            {
                yield return new AnalyzerFinding(code, fact.Location.Range, DiagnosticSeverity.Warning, AnalyzerMessages.Format(message, SymbolUri.NameOf(binding)));
            }
        }
    }

    private static DiagnosticFixes? FixOf(ModuleNode module, ExternalMemberDeclarationNode statement)
    {
        var range = statement.SourceLocation.Range;
        var (first, last) = (range.Start.Line, range.End.Line);
        // the statement alone on its lines: the directives go on lines of their own, before it and after it.
        if (module.Descendants().Any(node => node != statement && node is not ModuleNode && !Contains(range, node.SourceLocation.Range.Start)
            && !Contains(node.SourceLocation.Range, range.Start) && (node.SourceLocation.Range.End.Line == first || node.SourceLocation.Range.Start.Line == last)))
        {
            return null;
        }

        var constant = PlatformOf(statement.Library);
        var indent = new string(' ', range.Start.Character);
        return new DiagnosticFixes
        {
            Edits = [SyntaxEdits.Fix(AnalyzerMessages.Format(RDCoreDiagnosticsResources.NativeCallReachableOnEveryPlatform_Fix, constant),
            [
                SyntaxEdits.Insert(new SourcePosition(first, 0), $"{indent}#If {constant} Then{NewLine}"),
                SyntaxEdits.Insert(new SourcePosition(last + 1, 0), $"{indent}#End If{NewLine}"),
            ])],
        };
    }

    // the platform a library is of, by the way its name is written.
    private static string PlatformOf(string library)
    {
        var name = library.Trim('"');
        return name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) || name.Contains(".framework", StringComparison.OrdinalIgnoreCase) ? "Mac"
            : name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || name.Contains(".so.", StringComparison.OrdinalIgnoreCase) ? "Linux"
            : "Win32";
    }

    private static bool Contains(SourceRange range, SourcePosition position) => position.CompareTo(range.Start) >= 0 && position.CompareTo(range.End) <= 0;
}
