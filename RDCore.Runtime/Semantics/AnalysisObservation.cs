using RDCore.SDK.Semantics.Facts;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// The observer of one analysis, as the parts of a runtime pipeline share it: it passes on the facts the pipeline states, except
/// while the fact being stated is being described.
/// </summary>
/// <remarks>
/// <para>
/// Describing an operation or a conversion evaluates it again, because what the analysis says of an operation depends on how
/// its operands convert, and a conversion evaluated is a conversion observed. Those evaluations are not the code's. The part
/// of the pipeline that is describing a fact <see cref="Suspend">suspends</see> the observation for as long as it takes, which
/// is why every part of one pipeline shares one instance rather than each holding the observer.
/// </para>
/// <para>
/// Like the pipeline it belongs to, an observation is for one analysis at a time: it is not thread-safe.
/// </para>
/// </remarks>
/// <param name="observer">Told of every fact the pipeline states.</param>
public sealed class AnalysisObservation(IAnalysisObserver observer)
{
    private int _suspensions;

    /// <summary>
    /// Whether facts are being held back, because one is being described.
    /// </summary>
    public bool IsSuspended => _suspensions > 0;

    /// <summary>
    /// Tells the observer of a conversion, unless the observation is suspended.
    /// </summary>
    /// <param name="fact">The conversion.</param>
    public void OnConversion(ConversionFact fact)
    {
        if (!IsSuspended)
        {
            observer.OnConversion(fact);
        }
    }

    /// <summary>
    /// Tells the observer of an operation, unless the observation is suspended.
    /// </summary>
    /// <param name="fact">The operation.</param>
    public void OnOperation(OperatorFact fact)
    {
        if (!IsSuspended)
        {
            observer.OnOperation(fact);
        }
    }

    /// <summary>
    /// Holds back the facts until the returned scope is disposed.
    /// </summary>
    /// <returns>The scope to dispose when the fact has been described. Scopes nest.</returns>
    public Suspension Suspend()
    {
        _suspensions++;
        return new Suspension(this);
    }

    /// <summary>
    /// The span of time during which an observation holds its facts back.
    /// </summary>
    public readonly struct Suspension : IDisposable
    {
        private readonly AnalysisObservation? _observation;

        internal Suspension(AnalysisObservation observation) => _observation = observation;

        /// <summary>
        /// Resumes the observation, unless another suspension is still open.
        /// </summary>
        public void Dispose()
        {
            if (_observation is not null)
            {
                _observation._suspensions--;
            }
        }
    }
}
