using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Platform.Protocol;

namespace RDCore.SDK.Model.Diagnostics;

/// <summary>
/// What an analyzer is given of a module: the syntax tree, and the facts the platform's semantic analysis pass found out about it.
/// </summary>
/// <remarks>
/// An analyzer is a function of facts: the pass says what is (<strong>RD-VBAL §5.0.3</strong>), and the analyzer decides what is worth saying. A fact is stated
/// only when it is true, so an analyzer can rely on one that is there and has nothing to say about one that is not.
/// </remarks>
/// <param name="Document">The document being analyzed.</param>
/// <param name="ParseResult">The parsed module: the syntax tree, for where things are written.</param>
/// <param name="Semantics">
/// What the environment host found out about the module, or <see langword="null"/> when it has not: it is not part of the platform, or has not loaded the module.
/// </param>
public sealed record class ModuleAnalysisContext(Uri Document, ModuleParseResult ParseResult, ModuleSemanticsDto? Semantics);

/// <summary>
/// One thing an analyzer has to say about a module.
/// </summary>
/// <param name="Id">The diagnostic: what it is, and the code it is reported under.</param>
/// <param name="Range">Where in the document it is.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Message">What is worth saying about it, in the language of the reader.</param>
public sealed record class AnalyzerFinding(RDCoreDiagnosticId Id, SourceRange Range, DiagnosticSeverity Severity, string Message);

/// <summary>
/// Looks at a module and says what is worth saying about it (<strong>RD-VBAL §2.6.4</strong>).
/// </summary>
/// <remarks>
/// Implement this in an extension to add a diagnostic: the platform calls every analyzer it knows of for each document it diagnoses. An analyzer is a pure function
/// of the <see cref="ModuleAnalysisContext"/>: it does not evaluate code, and it does not resolve names - what resolving found out is among the facts it is given.
/// </remarks>
public interface IModuleAnalyzer
{
    /// <summary>
    /// What the analyzer has to say about the module.
    /// </summary>
    /// <param name="context">The module, and what is known of it.</param>
    /// <returns>Every finding, in no particular order; none when there is nothing to say.</returns>
    IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context);
}
