using RDCore.Parsing.Model.Types.Complex;
using RDCore.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

/// <summary>
/// Describes a UDT as a member of its parent module.
/// </summary>
internal record class VBUserDefinedTypeMember : VBTypeMemberSymbol
{
    public VBUserDefinedTypeMember(Uri workspaceUri, string name, Range range, Range selectionRange, Uri parentUri)
        : base(workspaceUri, name, SymbolKindExt.Field, Accessibility.Public, parentUri, range, selectionRange)
    {
    }
}
