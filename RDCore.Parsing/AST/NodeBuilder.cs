using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using System.Collections.Immutable;

namespace RDCore.Parsing.AST;

internal abstract class NodeBuilder(Uri rootUri, SyntaxNodeId nodeId)
{
    protected readonly Uri _rootUri = rootUri;
    protected readonly List<SyntaxNode> _children = [];

    public SyntaxNodeId NodeId => nodeId;

    // deliberately independent of ChildCount: a statement that captures more than one isolated
    // expression in sequence (an assignment's target then value, Name's two paths, ...) pushes and
    // pops a separate temporary scope per capture via CaptureIsolated, none of which ever add
    // anything to THIS builder's own _children in between. ChildCount stays unchanged across both
    // calls, so every id minted from it collided at the exact same lineage - confirmed empirically
    // even for the simplest `x = y` (both SimpleNameExpressionNodes got the identical id). This
    // counter advances on every id allocation, not just on a permanent AddChild, so no two nodes
    // built anywhere under this scope can ever share one - including a node whose containing capture
    // is later discarded (recovery) rather than kept.
    private int _nextChildId;
    public SyntaxNodeId AllocateChildId() => nodeId.Add(_nextChildId++);

    // parses a `visibility` token to an AccessModifier. VBA keywords are case-insensitive, so any
    // casing binds; anything unrecognized (or null, under recovery) is Implicit.
    internal static AccessModifier ParseAccessModifier(string? visibility)
        => Enum.TryParse<AccessModifier>(visibility, ignoreCase: true, out var modifier)
            ? modifier
            : AccessModifier.Implicit;

    public void AddChild(SyntaxNode node) => _children.Add(node);

    public SyntaxNode? LastChild => _children.Count == 0 ? null : _children.Last();
    public IEnumerable<SyntaxNode> GetChildren => _children.AsEnumerable();

    // for a left-recursive grammar rule (the `expression`/`lExpression` family), AddParseListener
    // fires Exit *before* Enter on an operator alternative — the opposite of a normal rule — so an
    // Enter/Exit push/pop pair can't scope an operator's operands (see ModuleParser's ParseOnce
    // remarks). An operator's own operands are, at Exit time, *usually* exactly the last `count`
    // nodes already added to whatever builder is currently active — but ANTLR error recovery can
    // leave fewer than that (a truncated `a = 1 +`, an unclosed `Foo x:=`), so both accessors below
    // clamp to what's actually there instead of indexing past it. `PeekLastChildren` lets a caller
    // check the *shape* it got (right count, right node types) before committing to `PopLastChildren`
    // — recovering gracefully, rather than only avoiding a crash, needs that: popping first and
    // discarding a malformed result would still destroy whatever legitimately parsed content was
    // sitting there.
    public ImmutableArray<SyntaxNode> PeekLastChildren(int count)
    {
        var actualCount = Math.Min(count, _children.Count);
        return [.. _children.GetRange(_children.Count - actualCount, actualCount)];
    }
    public ImmutableArray<SyntaxNode> PopLastChildren(int count)
    {
        var popped = PeekLastChildren(count);
        _children.RemoveRange(_children.Count - popped.Length, popped.Length);
        return popped;
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
