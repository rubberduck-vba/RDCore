using RDCore.Runtime.LanguageCore.Instructions;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics.Facts;
using RDCore.Runtime.Semantics;
using System.Collections.Immutable;

namespace RDCore.Runtime.Execution;

/// <summary>
/// Evaluates the procedures of a module to find out what the language core states about their code (<see cref="RuntimeFacts"/>), without running them.
/// </summary>
/// <remarks>
/// <para>
/// Every procedure is an entry point of its own, and every instruction of it is evaluated once, wherever the code paths lead
/// (<see cref="ProcedureExecutor.Sweep"/>): the branch nobody takes and the procedure nothing calls are seen like the rest. Nothing that is assumed
/// is stated as a fact (<see cref="RuntimeExecutionPipeline.CreateForSweep"/>).
/// </para>
/// <para>
/// The facts are those of the module's own code. Its code also evaluates expressions another module declares (the value of a constant or of an enumeration
/// member, the default of an optional parameter), and what such an expression is, is a fact of the module that declares it, not of this one: a fact located
/// in another document is not stated.
/// </para>
/// <para>
/// It happens in two steps because it is not done where it is asked for. <see cref="Prepare"/> is done on the thread that owns the session the program runs
/// in, and takes what the evaluation needs of it as it is at that moment (<see cref="RuntimeSessionComposer.ComposeForAnalysis"/>); <see cref="Evaluate"/> then
/// reads nothing but what <see cref="Prepare"/> took, and can run on any thread, while the session goes on changing.
/// </para>
/// </remarks>
public sealed class ModuleSweep
{
    private readonly IRuntimeSession _analysis;
    private readonly Uri _document;
    private readonly IReadOnlyList<(VBTypeMemberSymbol Procedure, InstructionList Body)> _procedures;
    private readonly IErrorDetailsRecorder _errorDetails;
    private readonly ModuleNode _syntaxTree;
    private readonly Uri _module;

    private ModuleSweep(
        IRuntimeSession analysis, Uri document, IReadOnlyList<(VBTypeMemberSymbol Procedure, InstructionList Body)> procedures,
        IErrorDetailsRecorder errorDetails, ModuleNode syntaxTree, Uri module)
    {
        _analysis = analysis;
        _document = document;
        _procedures = procedures;
        _errorDetails = errorDetails;
        _syntaxTree = syntaxTree;
        _module = module;
    }

    /// <summary>
    /// Takes what the evaluation of <paramref name="procedures"/> needs of <paramref name="session"/>, as it is now.
    /// </summary>
    /// <param name="session">A session whose symbols are declared: the context the code is evaluated in. It is not changed.</param>
    /// <param name="document">The document the module's code is in, where each of its facts is located.</param>
    /// <param name="procedures">The procedures to evaluate with their lowered bodies.</param>
    /// <param name="errorDetails">Records the details of the errors the evaluation raises.</param>
    /// <param name="syntaxTree">The module's syntax tree: what the classes of the object of a late-bound member access are found in.</param>
    /// <param name="module">The address of the module's symbol.</param>
    public static ModuleSweep Prepare(
        IRuntimeSession session, Uri document, IReadOnlyList<(VBTypeMemberSymbol Procedure, InstructionList Body)> procedures,
        IErrorDetailsRecorder errorDetails, ModuleNode syntaxTree, Uri module)
        => new(RuntimeSessionComposer.ComposeForAnalysis(session), document, procedures, errorDetails, syntaxTree, module);

    /// <summary>
    /// Evaluates the procedures.
    /// </summary>
    /// <param name="cancellation">
    /// Stops the evaluation between instructions, and when it makes an element of a fixed-size array a procedure declares.
    /// </param>
    /// <returns>
    /// The facts of each procedure, by the <see cref="Symbol.SemanticId"/> of the procedure. A procedure that could not be evaluated in full has the facts
    /// that were stated before it stopped, and says it was not fully analyzed.
    /// </returns>
    /// <exception cref="OperationCanceledException">The evaluation was cancelled: there is no answer to give, and what was found is not kept.</exception>
    public ImmutableDictionary<SemanticId, RuntimeFacts> Evaluate(CancellationToken cancellation = default)
    {
        var facts = ImmutableDictionary.CreateBuilder<SemanticId, RuntimeFacts>();
        if (_procedures.Count == 0)
        {
            return facts.ToImmutable();
        }

        var collector = new AnalysisFactCollector();
        var nodes = new Lazy<IReadOnlyDictionary<SyntaxNodeId, SyntaxNode>>(
            () => _syntaxTree.Descendants().DistinctBy(node => node.Identity).ToDictionary(node => node.Identity));
        var bodies = _procedures.ToDictionary(entry => entry.Procedure.SemanticId, entry => entry.Body);
        var pipeline = RuntimeExecutionPipeline.CreateForSweep(_analysis, bodies, _errorDetails, new FactsOfTheDocument(_document, collector), cancellation);

        foreach (var (procedure, _) in _procedures)
        {
            cancellation.ThrowIfCancellationRequested();

            var complete = false;
            try
            {
                complete = pipeline.Sweeper!.Sweep(procedure);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // a defect in the evaluation is the platform's, not the code's: what was stated before it is true, and the procedure says it is not all.
                complete = false;
            }

            // a sweep that was stopped did not finish the procedure it was in, and there is no telling which of the others it had not yet reached.
            cancellation.ThrowIfCancellationRequested();

            // the sweep follows no path, and says nothing of the classes the code may give the object of a late-bound access: the module's Set statements do.
            facts[procedure.SemanticId] = LateBoundCandidateFacts.Fill(collector.Take(complete), _analysis, _module, _syntaxTree, nodes);
        }

        return facts.ToImmutable();
    }

    // tells the collector of each fact located in the module's document, and of none located in another.
    private sealed class FactsOfTheDocument(Uri document, IAnalysisObserver collector) : IAnalysisObserver
    {
        public void OnConversion(ConversionFact fact)
        {
            if (IsInTheDocument(fact.Location))
            {
                collector.OnConversion(fact);
            }
        }

        public void OnOperation(OperatorFact fact)
        {
            if (IsInTheDocument(fact.Location))
            {
                collector.OnOperation(fact);
            }
        }

        public void OnStatement(StatementFact fact)
        {
            if (IsInTheDocument(fact.Location))
            {
                collector.OnStatement(fact);
            }
        }

        public void OnError(VBErrorInfo error)
        {
            if (IsInTheDocument(error.Location))
            {
                collector.OnError(error);
            }
        }

        // a fact that says no document is the code's: it is not another module's either.
        private bool IsInTheDocument(SourceLocation location) => location.Uri is null || location.Uri == document;
    }
}
