using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;

namespace RDCore.Parsing.AST;

internal abstract class NodeBuilder(Uri rootUri, SyntaxNodeId nodeId)
{
    protected readonly Uri _rootUri = rootUri;
    protected readonly List<SyntaxNode> _children = [];

    public SyntaxNodeId NodeId => nodeId;

    // parses a `visibility` token to an AccessModifier. VBA keywords are case-insensitive, so any
    // casing binds; anything unrecognized (or null, under recovery) is Implicit.
    internal static AccessModifier ParseAccessModifier(string? visibility)
        => Enum.TryParse<AccessModifier>(visibility, ignoreCase: true, out var modifier)
            ? modifier
            : AccessModifier.Implicit;

    public void AddChild(SyntaxNode node) => _children.Add(node);

    public int ChildCount => _children.Count;
    public SyntaxNode? LastChild => _children.Count == 0 ? null : _children.Last();
    public IEnumerable<SyntaxNode> GetChildren => _children.AsEnumerable();
    public void UpdateLastChild(SyntaxNode node)
    {
        if (node.GetType() != _children.Last().GetType())
        {
            // that would be an arbitrary replacement, most likely a bug.
            throw new InvalidOperationException();
        }
        _children.RemoveAt(_children.Count - 1);
        _children.Add(node);
    }
}
