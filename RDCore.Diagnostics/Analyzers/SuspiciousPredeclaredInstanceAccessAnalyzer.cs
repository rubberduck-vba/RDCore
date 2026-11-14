using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Diagnostics.Model;
using RDCore.SDK.Extensibility.Diagnostics;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics.Flags;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// <c>RDC00410</c>: the code of a class module names the default instance of the class (<strong>MS-VBAL §5.2.4.1.2</strong>): <c>Factory.Init</c> in the
/// class <c>Factory</c>. The object that runs the code is <c>Me</c>, which is the default instance only when the code was called on it.
/// </summary>
/// <remarks>
/// The host binds the name to the default instance variable, which the global scope declares (<see cref="StaticSymbol.GlobalUri"/>); the class itself, named as
/// a type (<c>New Factory</c>, <c>As Factory</c>), is bound to the class module.
/// </remarks>
internal sealed class SuspiciousPredeclaredInstanceAccessAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
    {
        if (context is not { Semantics: { Class: { } flags } semantics } || !flags.HasFlag(ClassModuleSemanticFlags.DefaultInstance))
        {
            return [];
        }

        var name = SymbolUri.ModuleNameOf(semantics.Module);
        return semantics.Procedures
            .SelectMany(procedure => procedure.Expressions)
            .Where(fact => IsTheDefaultInstance(fact.Binding, name))
            .OrderBy(fact => fact.Location.Range.Start)
            .Select(fact => new AnalyzerFinding(
                RDCoreDiagnosticId.SuspiciousPredeclaredInstanceAccess.ToCode(),
                fact.Location.Range,
                DiagnosticSeverity.Warning,
                AnalyzerMessages.Format(RDCoreDiagnosticsResources.SuspiciousPredeclaredInstanceAccess_Message, name)));
    }

    private static bool IsTheDefaultInstance(Uri? binding, string className)
        => binding is not null
            && Uri.Compare(binding, StaticSymbol.GlobalUri, UriComponents.SchemeAndServer | UriComponents.Path, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0
            && string.Equals(SymbolUri.NameOf(binding), className, StringComparison.OrdinalIgnoreCase);
}
