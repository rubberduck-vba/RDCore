using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Abstract;

internal record class ValuedExpression(Location location) : BoundExpression(location)
{
    public VBTypedValue ResultValue { get; init; } = VBEmptyValue.Empty;

    public ValuedExpression WithResultValue(VBTypedValue value) => this with { ResultValue = value, ResultType = value.TypeInfo };
}