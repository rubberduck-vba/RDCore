using RDCore.Runtime.LanguageCore.Instructions;
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
/// It happens in two steps because it is not done where it is asked for. <see cref="Prepare"/> is done on the thread that owns the session the program runs
/// in, and takes what the evaluation needs of it as it is at that moment (<see cref="RuntimeSessionComposer.ComposeForAnalysis"/>); <see cref="Evaluate"/> then
/// reads nothing but what <see cref="Prepare"/> took, and can run on any thread, while the session goes on changing.
/// </para>
/// </remarks>
public sealed class ModuleSweep
{
    private readonly IRuntimeSession _analysis;
    private readonly IReadOnlyList<(VBTypeMemberSymbol Procedure, InstructionList Body)> _procedures;
    private readonly IErrorDetailsRecorder _errorDetails;

    private ModuleSweep(IRuntimeSession analysis, IReadOnlyList<(VBTypeMemberSymbol Procedure, InstructionList Body)> procedures, IErrorDetailsRecorder errorDetails)
    {
        _analysis = analysis;
        _procedures = procedures;
        _errorDetails = errorDetails;
    }

    /// <summary>
    /// Takes what the evaluation of <paramref name="procedures"/> needs of <paramref name="session"/>, as it is now.
    /// </summary>
    /// <param name="session">A session whose symbols are declared: the context the code is evaluated in. It is not changed.</param>
    /// <param name="procedures">The procedures to evaluate with their lowered bodies.</param>
    /// <param name="errorDetails">Records the details of the errors the evaluation raises.</param>
    public static ModuleSweep Prepare(
        IRuntimeSession session, IReadOnlyList<(VBTypeMemberSymbol Procedure, InstructionList Body)> procedures, IErrorDetailsRecorder errorDetails)
        => new(RuntimeSessionComposer.ComposeForAnalysis(session), procedures, errorDetails);

    /// <summary>
    /// Evaluates the procedures.
    /// </summary>
    /// <param name="cancellation">Stops the evaluation between instructions.</param>
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
        var bodies = _procedures.ToDictionary(entry => entry.Procedure.SemanticId, entry => entry.Body);
        var pipeline = RuntimeExecutionPipeline.CreateForSweep(_analysis, bodies, _errorDetails, collector, cancellation);

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

            facts[procedure.SemanticId] = collector.Take(complete);
        }

        return facts.ToImmutable();
    }
}
