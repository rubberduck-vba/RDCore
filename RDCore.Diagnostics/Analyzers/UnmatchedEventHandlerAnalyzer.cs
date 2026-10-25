using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Semantics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00407</c>: a procedure named as the handler of an event of a form's control (<c>TextBox1_Change</c>) that does not match the event, and that nothing
/// calls: it is dead code.
/// </summary>
/// <remarks>
/// <para>
/// MS-VBA compiles a handler of a control's event that is not a subroutine or whose parameter list is not the event's, and the event never calls it
/// (Excel-measured). What it is, the host says (<see cref="DeclarationRole.UnmatchedEventHandler"/>), and whether the module's own code calls it as the procedure
/// it also is (<see cref="DeclarationFact.References"/>): a Private one that nothing calls is dead, and is reported over the whole of its declaration, tagged
/// <see cref="DiagnosticTag.Unnecessary"/>, which a client renders as it renders code that cannot be reached. A Public one can be called through the form, by code
/// the pass does not see, and is not reported.
/// </para>
/// </remarks>
internal sealed class UnmatchedEventHandlerAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => (context.Semantics?.Declarations ?? [])
            .Where(declaration => declaration is { Role: DeclarationRole.UnmatchedEventHandler, References.Total: 0 })
            .Select(declaration => new AnalyzerFinding(
                RDCoreDiagnosticId.UnmatchedEventHandler,
                RangeOf(context, declaration),
                DiagnosticSeverity.Hint,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.UnmatchedEventHandler_Message, declaration.Name))
            {
                Tags = [DiagnosticTag.Unnecessary],
            });

    // the whole of the procedure is dead, and not only its name
    private static SDK.Model.Source.SourceRange RangeOf(ModuleAnalysisContext context, DeclarationFactDto declaration)
        => context.ParseResult.SyntaxTree?.Children.OfType<MemberDeclarationNode>()
            .FirstOrDefault(member => string.Equals(member.Name, declaration.Name, StringComparison.OrdinalIgnoreCase))?.SourceLocation.Range
           ?? declaration.Location.Range;
}
