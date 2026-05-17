using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

internal record class VBPropertySetMember : VBProcedureMember, IVBProperty
{
    public VBPropertySetMember(Uri workspaceUri, string name, Accessibility accessibility, Uri parentUri)
        : base(workspaceUri, SymbolKindExt.Property, name, accessibility, parentUri)
    {
    }

    public VBPropertySetMember(Uri workspaceUri, string name, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(workspaceUri, SymbolKindExt.Property, name, accessibility, parentUri, range, selectionRange)
    {
    }
}
