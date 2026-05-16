using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Workspace;
using System.Collections.Immutable;

namespace RDCore.Runtime;

internal sealed class VBExecutionContext(WorkspaceDocument document, VirtualHeap memory)
{
    required public bool Is64Bit { get; init; }

    public WorkspaceDocument Document { get; } = document;
    public VirtualHeap Memory { get; } = memory;

    private readonly List<Diagnostic> _diagnostics = [];
    public ImmutableArray<Diagnostic> Diagnostics => [.. _diagnostics];
    public ScopeContext CurrentScope { get; private set; } = default!;

    public void AddDiagnostic(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic);
    }

    // Allows the Binder to "Push/Pop" as it enters Procedures
    public IDisposable EnterScope(Symbol scopeSymbol)
    {
        var previous = CurrentScope;
        CurrentScope = new ScopeContext(scopeSymbol, previous);
        return new ScopeReliever(this, previous);
    }

    private record ScopeReliever(VBExecutionContext Context, ScopeContext Previous) : IDisposable
    {
        public void Dispose() => Context.CurrentScope = Previous;
    }
}
