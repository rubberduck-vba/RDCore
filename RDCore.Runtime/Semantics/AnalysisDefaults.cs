using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.Runtime.Semantics;

/// <summary>
/// What a variable starts as in an <em>analysis</em>: its declared default where the code that runs makes it, and a value that is not known
/// where the rest of the program does.
/// </summary>
/// <remarks>
/// <para>
/// A local starts as its declared default at every activation, and so does a field of an object, when the object is made: the default is what the
/// code gives it, so it is known. A variable that outlives an activation - a module variable, a global, a <c>Static</c> local - is entered
/// with whatever the rest of the program last left in it, which an analysis of one procedure cannot know. It starts as a value that is not known, of its
/// declared type (and, for an array, of the shape it is declared with).
/// </para>
/// <para>
/// A parameter is not a variable that starts as anything: it is bound to its argument. The one exception is an optional one whose argument the
/// call omits, which starts as its declared default - a constant, which the call that runs gives it, so it is known.
/// </para>
/// </remarks>
/// <param name="declared">Says what a variable is declared as: an array, its dimensions.</param>
public sealed class AnalysisDefaults(IVariableDefaults declared) : IVariableDefaults
{
    /// <inheritdoc/>
    public VBTypedValue DefaultValueOf(Symbol variable)
    {
        var value = declared.DefaultValueOf(variable);
        return IsMadeByTheCodeThatRuns(variable) ? value : value.AsIndeterminate();
    }

    private static bool IsMadeByTheCodeThatRuns(Symbol variable)
        => variable.ScopeKind is ScopeKind.Instance || variable is VBLocalVariableSymbol { IsStatic: false } or VBParameterSymbol;
}
