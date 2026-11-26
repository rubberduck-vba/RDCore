using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00221</c>: a call of the <c>VBA</c> library's <c>Array</c> function (<c>VBA.Array(...)</c>) in a module with <c>Option Base 1</c>. The library's
/// function returns an array with a lower bound of 0. The unqualified <c>Array(...)</c> is the keyword, whose array has the lower bound that
/// <c>Option Base</c> gives (<strong>MS-VBAL §5.2.1.2</strong>, <strong>RD-VBAL §6.1.2.13</strong>).
/// </summary>
/// <remarks>
/// The analyzer reads the calls that the module's semantics bind to the library's hidden module, and the file module's own <c>Option Base</c>.
/// </remarks>
internal sealed class QualifiedArrayOptionBaseAnalyzer : IModuleAnalyzer
{
    private const string LibraryArray = "_HiddenModule.Array";

    public IReadOnlyList<RDCoreDiagnosticCode> Codes => [RDCoreDiagnosticId.QualifiedArrayIgnoresOptionBase.ToCode()];

    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context.ParseResult.SyntaxTree is not { } module || context.Semantics is not { } semantics
            || module.GetOptionBase() != 1)
        {
            return [];
        }

        return semantics.Procedures.SelectMany(procedure => procedure.Expressions)
            .Where(fact => fact is { Binding: { } binding }
                && fact.Flags.HasFlag(ValueExpressionSemanticFlags.BoundToLibrary | ValueExpressionSemanticFlags.ProcedureCall)
                && string.Equals(SymbolUri.QualifiedNameOf(binding), LibraryArray, StringComparison.OrdinalIgnoreCase))
            // the callee VBA.Array and the call VBA.Array(...) are bound alike: the call is reported, at the widest range that starts there.
            .GroupBy(fact => fact.Location.Range.Start)
            .Select(call => call.MaxBy(fact => (fact.Location.Range.End.Line, fact.Location.Range.End.Character))!)
            .Select(fact => new AnalyzerFinding(RDCoreDiagnosticId.QualifiedArrayIgnoresOptionBase.ToCode(), fact.Location.Range,
                DiagnosticSeverity.Hint, RDCoreDiagnosticsResources.QualifiedArrayIgnoresOptionBase_Message));
    }
}
