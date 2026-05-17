using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

internal record class VBFunctionMember : VBReturningMember
{
    protected VBFunctionMember(Uri workspaceRoot, string name, Accessibility accessibility, Uri parentUri)
        : base(workspaceRoot, name, accessibility, SymbolKindExt.Function, parentUri)
    {
    }

    protected VBFunctionMember(Uri workspaceRoot, string name, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(workspaceRoot, name, accessibility, SymbolKindExt.Function, parentUri, range, selectionRange)
    {
    }
}
