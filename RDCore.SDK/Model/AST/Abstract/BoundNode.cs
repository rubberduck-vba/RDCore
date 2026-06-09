using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Semantics;

namespace RDCore.SDK.Model.AST.Abstract;

/// <summary>
/// An <em>abstract syntax tree</em> (AST) node that can attach a specific type of <em>semantic context</em>, with <em>semantic flags</em> and <see cref="VBErrorInfo"/> metadata.
/// </summary>
/// <typeparam name="TContext">The type of <see cref="SemanticContext{TFlags}"/> attached to this node.</typeparam>
/// <typeparam name="TFlags">A <c>enum</c> type with bit-shifted <c>[Flags]</c> members that combine to encode <em>semantic flags</em> (facts) about a <em>semantic operation</em>.</typeparam>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of this node.</param>
public abstract record class BoundNode<TContext, TFlags>(Uri SemanticId, Location Location)
where TContext : SemanticContext<TFlags>, new()
where TFlags : struct, Enum
;

/// <summary>
/// A <c>BoundNode</c> that can be statically evaluated to a <c>VBType</c>, and with runtime semantics to a <c>VBTypedValue</c>.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public abstract record class BoundExpressionNode<TContext, TFlags>(Uri SemanticId, Location Location) : BoundNode<TContext, TFlags>(SemanticId, Location)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
;

/// <summary>
/// A <c>BoundNode</c> representing an <em>executable statement</em>.
/// </summary>
/// <param name="SemanticId">A semantic <c>Uri</c> uniquely identifying this specific node.</param>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public abstract record class BoundStatementNode<TContext, TFlags>(Uri SemanticId, Location Location, Symbol ResultSymbol) : BoundNode<TContext, TFlags>(SemanticId, Location)
    where TContext : SemanticContext<TFlags>, new()
    where TFlags : struct, Enum
;
