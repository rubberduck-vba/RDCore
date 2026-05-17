using RDCore.Parsing.Model.Expressions.Abstract;
using RDCore.Parsing.Model.Symbols.Abstract;
using RDCore.Parsing.Model.Types.Intrinsic;
using RDCore.SDK.Server.ProtocolExtensions;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace RDCore.Parsing.Model.Symbols;

internal record class VBEnumMember : VBReturningMember
{
    public VBEnumMember(Uri workspaceRoot, string name, Uri parentUri, BoundExpression valueExpression)
        : base(workspaceRoot, name, Accessibility.Public, SymbolKindExt.EnumMember, parentUri)
    {
        ResolvedType = VBLongType.TypeInfo;
        ValueExpression = valueExpression;
    }

    public VBEnumMember(Uri workspaceRoot, string name, Uri parentUri, BoundExpression? valueExpression, Range range, Range selectionRange)
        : base(workspaceRoot, name, Accessibility.Public, SymbolKindExt.EnumMember, parentUri, range, selectionRange)
    {
        ResolvedType = VBLongType.TypeInfo;
        ValueExpression = valueExpression;
    }

    public BoundExpression? ValueExpression { get; init; }
}
