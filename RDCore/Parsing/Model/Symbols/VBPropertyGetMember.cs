using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

internal interface IVBProperty
{
    string Name { get; }
}

internal record class VBPropertyGetMember : VBReturningMember, IVBProperty
{
    public VBPropertyGetMember(Uri workspaceRoot, string name, Accessibility accessibility, Uri parentUri)
        : base(workspaceRoot, name, accessibility, SymbolKindExt.Property, parentUri)
    {
    }

    public VBPropertyGetMember(Uri workspaceRoot, string name, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(workspaceRoot, name, accessibility, SymbolKindExt.Property, parentUri, range, selectionRange)
    {
    }
}
