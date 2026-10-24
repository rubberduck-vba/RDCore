using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.StdLib;

/// <summary>
/// Calls a member of the standard library on its implementation, with the arguments marshalled for it (<see cref="StdLibDispatcher"/>).
/// </summary>
/// <param name="implementation">The implementation of the member's interface.</param>
/// <param name="arguments">The arguments, one for each parameter the member declares: a value of its type, or <see langword="null"/> for one left out.</param>
/// <returns>What the member returned.</returns>
internal delegate RuntimeSemanticsEvaluationResult StdLibInvoker(object implementation, object?[] arguments);

/// <summary>
/// The invoker of each member of the standard library's declarations, generated from them (IR lowering S1.9, <c>RDCore.Runtime.Generators</c>): the
/// interface method called itself, with no reflection when the program runs.
/// </summary>
internal static partial class StdLibInvokers
{
    /// <summary>
    /// The invoker of the member the symbol reader keyed <paramref name="target"/> (<c>SymbolProperties.ExternalTarget</c>).
    /// </summary>
    /// <returns><see langword="null"/> for a member none is generated for, which is invoked by reflection.</returns>
    public static partial StdLibInvoker? For(string target);

    // what a member returned, of the type it declares, as the dispatcher returns it: generic on the result, which is a struct, so that it is not boxed.
    private static RuntimeSemanticsEvaluationResult Of<TResult>(TResult result) where TResult : IRuntimeSemanticsEvaluationResult
        => new(result.Result, result.ErrorInfo);
}
