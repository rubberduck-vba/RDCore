using RDCore.SDK.Model.Source;
using RDCore.Runtime.LanguageCore.Binding;

namespace RDCore.Runtime.Compiled;

// where an operation compiled code evaluates raises the error it finds: at the operation's expression, as the interpreter raises it.
internal sealed partial class ProcedureAnalysis
{
    private readonly List<SourceLocation> _arithmeticSites = [];

    /// <summary>
    /// Where each integral operation of the body is written, and each extended intermediate it converts, by the number compiled code knows it by: the error
    /// 6 (Overflow) or 11 (Division by zero) it raises is raised there (MS-VBAL §5.6.9.3).
    /// </summary>
    public IReadOnlyList<SourceLocation> ArithmeticSites => _arithmeticSites;

    private int ArithmeticSiteOf(BoundNode node)
    {
        _arithmeticSites.Add(node.Expression.SourceLocation);
        return _arithmeticSites.Count - 1;
    }
}
