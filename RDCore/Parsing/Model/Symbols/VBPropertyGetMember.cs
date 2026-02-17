using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

internal interface IVBProperty
{
    string Name { get; }
}

internal record class VBPropertyGetMember : VBReturningMember, IVBProperty
{
    public VBPropertyGetMember(Uri workspaceRoot, string name, Accessibility accessibility, Uri parentUri, Range? range = default, Range? selectionRange = default, bool isHidden = false)
        : base(workspaceRoot, name, accessibility, SymbolKindExt.Property, parentUri, range, selectionRange, isHidden)
    {
    }
}
