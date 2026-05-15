using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model;
using RDCore.Parsing.Model.Types;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model;

internal abstract record class ValuedExpression : BoundExpression
{
    protected internal ValuedExpression(Location location, VBTypedValue? resultValue = null)
        : base(location)
    {
        ResultValue = resultValue;
        StaticDeclaredType = resultValue?.TypeInfo ?? UnresolvedType.TypeInfo;
    }

    public VBTypedValue? ResultValue { get; init; }

    public ValuedExpression WithResultValue(VBTypedValue value) => this with { ResultValue = value, StaticDeclaredType = value.TypeInfo };
}