using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

internal record class VBPropertyLetMember : VBProcedureMember, IVBProperty
{
    public VBPropertyLetMember(Uri workspaceUri, string name, Accessibility accessibility, Uri parentUri)
        : base(workspaceUri, SymbolKindExt.Property, name, accessibility, parentUri)
    {
    }

    public VBPropertyLetMember(Uri workspaceUri, string name, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(workspaceUri, SymbolKindExt.Property, name, accessibility, parentUri, range, selectionRange)
    {
    }
}
