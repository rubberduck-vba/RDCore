namespace RDCore.SDK.Semantics.Facts;

/// <summary>
/// Receives the <em>facts</em> that the runtime semantics state about the code they evaluate: the conversions it asks for and the
/// operations it performs.
/// </summary>
/// <remarks>
/// <para>
/// An observer is attached to a runtime pipeline when the pipeline is composed, and sees every conversion and operation that
/// pipeline evaluates, at the point where the evaluation would call the semantics. That is what makes what is observed
/// the language's own answer, rather than a second walk of the code that must be kept in step with the first.
/// </para>
/// <para>
/// A pipeline composed without an observer states no facts and behaves exactly as before: observation costs a null check.
/// An observer must not throw, and must not call back into the pipeline that is calling it.
/// </para>
/// <para>
/// A pipeline and its observer belong to one analysis: an observer is not required to be thread-safe.
/// </para>
/// </remarks>
public interface IAnalysisObserver
{
    /// <summary>
    /// A value was let-coerced (<strong>MS-VBAL 5.5.1.2</strong>).
    /// </summary>
    /// <param name="fact">What the conversion is, and what is known of it.</param>
    void OnConversion(ConversionFact fact);

    /// <summary>
    /// An operator was evaluated (<strong>MS-VBAL 5.6.9</strong>).
    /// </summary>
    /// <param name="fact">What the operation is, and what is known of it.</param>
    void OnOperation(OperatorFact fact);
}
