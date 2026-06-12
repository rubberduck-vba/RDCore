using RDCore.SDK.Model.Symbols.Abstract;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.SDK.Model.Symbols;

/// <summary>
/// Represents a semantically <c>Static</c> local variable symbol.
/// </summary>
/// <remarks>
/// <c>Static</c> 
/// locals are allocated at module level in the <em>workspace statics</em> heap.
/// A local variable is <c>Static</c> if its parent scope declaration includes the <c>Static</c> modifier, or if the <c>Static</c> keyword is used to declare that variable.
/// </remarks>
/// <param name="WorkspaceRoot">The workspace root for this symbol. For an external project or library, this should be different than the user's project workspace.</param>
/// <param name="ParentUri">The <c>Uri</c> of the parent symbol.</param>
/// <param name="Name">The identifier name of the symbol.</param>
/// <param name="Range">A <c>Range</c> pointing to the document location that belongs to this symbol.</param>
/// <param name="SelectionRange">A <c>Range</c> pointing to the document location that should be selected when navigating to this symbol.</param>
public sealed record VBStaticLocalVariableSymbol(Uri WorkspaceRoot, Uri ParentUri, string Name, Range Range, Range SelectionRange)
    : VBLocalVariableSymbol(WorkspaceRoot, ParentUri, Name, ScopeKind.Local, Range, SelectionRange, IsStatic: true){ }
