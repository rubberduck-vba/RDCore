using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

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

    // for a left-recursive grammar rule (the `expression`/`lExpression` family), AddParseListener
    // fires Exit *before* Enter on an operator alternative — the opposite of a normal rule — so an
    // Enter/Exit push/pop pair can't scope an operator's operands (see ModuleParser's ParseOnce
    // remarks). An operator's own operands are, at Exit time, always exactly the last `count` nodes
    // already added to whatever builder is currently active — nothing else can have interleaved
    // since expression subtrees resolve depth-first. Popping them back out (in original order) and
    // pushing the combined operator node in their place sidesteps the ordering bug entirely.
    public ImmutableArray<SyntaxNode> PopLastChildren(int count)
    {
        var start = _children.Count - count;
        var popped = _children.GetRange(start, count);
        _children.RemoveRange(start, count);
        return [.. popped];
    }
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
