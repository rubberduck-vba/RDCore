namespace RDCore.SDK.Runtime.Abstract.Execution;

/// <summary>
/// The symbols of a running session, as the code it runs sees them: what a name binds to (<see cref="ISymbolResolver"/>), and the storage each
/// symbol is bound to (<see cref="ISymbolStorage"/>).
/// </summary>
/// <remarks>
/// What the run-time operations are given: a binding handle reads and writes its value through it, and a procedure is invoked with it.
/// </remarks>
public interface IRuntimeSymbolResolver : ISymbolResolver, ISymbolStorage
{
}
