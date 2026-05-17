using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

internal record class VBProcedureMember : VBTypeMemberSymbol
{
    protected VBProcedureMember(Uri workspaceUri, SymbolKindExt kind, string name, Accessibility accessibility, Uri parentUri)
        : base(workspaceUri, name, kind, accessibility, parentUri)
    {
        ResolvedType = VBVoidType.TypeInfo;
    }

    protected VBProcedureMember(Uri workspaceUri, SymbolKindExt kind, string name, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(workspaceUri, name, kind, accessibility, parentUri, range, selectionRange)
    {
        ResolvedType = VBVoidType.TypeInfo;
    }

    public VBProcedureMember(Uri workspaceUri, string name, Accessibility accessibility, Uri parentUri)
    : base(workspaceUri, name, SymbolKindExt.Procedure, accessibility, parentUri)
    {
        ResolvedType = VBVoidType.TypeInfo;
    }

    public VBProcedureMember(Uri workspaceUri, string name, Accessibility accessibility, Uri parentUri, Range range, Range selectionRange)
        : base(workspaceUri, name, SymbolKindExt.Procedure, accessibility, parentUri, range, selectionRange)
    {
        ResolvedType = VBVoidType.TypeInfo;
    }
}
