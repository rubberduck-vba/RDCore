using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types.Abstract;


namespace RDCore.SDK.Model.Values.Abstract;

/// <summary>
/// The symbol associated with the result of an operator expression.
/// </summary>
public record class OperatorExpressionValueSymbol : BoundTypedSymbol
{
    public OperatorExpressionValueSymbol(Uri workspaceRoot, Uri parentUri, StaticSymbol operatorSymbol, SourceRange range, SourceRange selectionRange, VBType resolvedType)
        : base(workspaceRoot, parentUri, $"{operatorSymbol.Name}@L{range.Start.Line}C{range.Start.Character}", ScopeKind.Local, SymbolKindExt.Operator, range, selectionRange, resolvedType)
    {
    }
}
