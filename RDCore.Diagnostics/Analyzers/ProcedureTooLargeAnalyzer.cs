using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using System.Globalization;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC11023</c>: a procedure of more than 5,000 statements, which MS-VBA refuses as "Procedure too large" and RD-VBA runs.
/// </summary>
/// <remarks>
/// MS-VBA refuses a procedure whose compiled code is larger than 64K (MS Learn). What that is in statements depends on the statements: 5,000 of <c>x = x + 1</c>
/// compile and 5,250 do not, 7,000 of <c>x = 1</c> compile and 8,000 do not, and 60,000 comment lines compile (recorded in Excel). RD-VBA compiles no p-code to
/// measure, and runs a procedure of any size; it warns past 5,000 statements, by the author's ruling, which is where MS-VBA starts to refuse procedures of the most
/// common statements. MS-VBAL sets no limit. Every statement of the procedure counts, in the blocks of others too; a comment is none.
/// </remarks>
internal sealed class ProcedureTooLargeAnalyzer : SyntaxTreeAnalyzer
{
    /// <summary>The most statements a procedure has before it is reported.</summary>
    public const int MaxStatements = 5_000;

    protected override IEnumerable<AnalyzerFinding> Analyze(ModuleNode module)
        => module.Descendants()
            .OfType<MemberDeclarationNode>()
            .Where(member => member.MemberKind is MemberKind.Procedure or MemberKind.Function or MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet)
            .Select(procedure => (Procedure: procedure, Statements: procedure.Descendants().OfType<StatementNode>().Count()))
            .Where(counted => counted.Statements > MaxStatements)
            .Select(counted => new AnalyzerFinding(
                RDCoreDiagnosticId.ProcedureTooLarge,
                counted.Procedure.NameRange ?? counted.Procedure.SourceLocation.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.ProcedureTooLarge_Message, counted.Procedure.Name,
                    counted.Statements.ToString("N0", CultureInfo.CurrentUICulture))));
}
