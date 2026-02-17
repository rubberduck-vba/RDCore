using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

internal record class VBFunctionMember : VBReturningMember
{
    protected VBFunctionMember(Uri workspaceRoot, string name, Accessibility accessibility, Uri parentUri, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(workspaceRoot, name, accessibility, SymbolKindExt.Function, parentUri, range, selectionRange, isHidden)
    {
    }
}
