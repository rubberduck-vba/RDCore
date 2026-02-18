using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Symbols;
using RDCore.Workspace;
using System.Collections.Immutable;

namespace RDCore.Runtime;

internal sealed class VBExecutionContext(WorkspaceDocument doc, SymbolTable globals)
{
    public WorkspaceDocument Document { get; } = doc;
    public SymbolTable GlobalSymbols { get; } = globals;

    private readonly List<Diagnostic> _diagnostics = [];
    public ImmutableArray<Diagnostic> Diagnostics => [.. _diagnostics];
    public ScopeContext CurrentScope { get; private set; } = default!;

    public void AddDiagnostic(Diagnostic diagnostic) => _diagnostics.Add(diagnostic);

    // Allows the Binder to "Push/Pop" as it enters Procedures or If-Blocks
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
