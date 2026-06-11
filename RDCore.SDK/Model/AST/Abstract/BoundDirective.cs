using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Semantics;

namespace RDCore.SDK.Model.AST.Abstract;


/// <summary>
/// A <see cref="BoundNode{TContext,TFlags}"/> representing a <em>module directive</em>, which is module metadata that is neither typed, nor executable.
/// </summary>
/// <remarks>
/// 🧩 They're still analyzable though.
/// </remarks>
/// <param name="Location">The document location (<c>Uri</c>+<c>Range</c>) of the bound expression.</param>
public abstract record class BoundDirective<TContext, TFlags>(Uri SemanticId, Location Location)
    : BoundNode<TContext, TFlags>(SemanticId, Location)
where TContext : SemanticContext<TFlags>, new()
where TFlags : struct, Enum;
