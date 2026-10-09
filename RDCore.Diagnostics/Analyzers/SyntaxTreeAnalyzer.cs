using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Diagnostics;

namespace RDCore.Diagnostics.Analyzers;

/// <summary>
/// An analyzer of how a module is <em>written</em>: what it says is a fact of the syntax tree, such as a keyword that is there or is not.
/// </summary>
/// <remarks>
/// What the code means - what a name resolves to, what an expression is converted to - is not written anywhere in the tree, and is read from the semantic facts
/// the host vouches for instead. A module that did not parse into a tree has nothing to say.
/// </remarks>
internal abstract class SyntaxTreeAnalyzer : IModuleAnalyzer
{
    public IEnumerable<AnalyzerFinding> Analyze(ModuleAnalysisContext context)
        => context.ParseResult.SyntaxTree is { } module ? Analyze(module) : [];

    /// <summary>
    /// What the analyzer has to say about the way the module is written.
    /// </summary>
    /// <param name="module">The syntax tree of the module.</param>
    protected abstract IEnumerable<AnalyzerFinding> Analyze(ModuleNode module);

    /// <summary>
    /// A message of the diagnostic, with the particulars of the occurrence in it.
    /// </summary>
    /// <param name="message">The localized text of the message, with <c>{0}</c> for the particular.</param>
    /// <param name="particular">What the occurrence is about: usually a name.</param>
    protected static string Say(string message, string particular) => AnalyzerMessages.Format(message, particular);
}
