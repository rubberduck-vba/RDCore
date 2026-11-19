using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.Symbols.Abstract;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §5.3.1.11: the parameter each argument a call may take by reference is given, where the static pass knows the procedure called. A count of the
// declaration the argument names follows it to find out whether the call can assign it (DeclarationUsage).
public sealed class ArgumentParameters
{
    private readonly Dictionary<SyntaxNodeId, SemanticId> _parameters = [];

    public void Record(SyntaxNodeId argument, SemanticId parameter) => _parameters[argument] = parameter;

    public bool TryGet(SyntaxNodeId argument, out SemanticId parameter) => _parameters.TryGetValue(argument, out parameter);
}
