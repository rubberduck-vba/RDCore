using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.Parsing.Model;
using RDCore.Parsing.Model.Values;

namespace RDCore.Runtime.Model;

internal abstract record class ValuedExpression(Location Location) : BoundExpression(Location)
{
    public VBTypedValue ResultValue { get; init; } = VBEmptyValue.Empty;

    public ValuedExpression WithResultValue(VBTypedValue value) => this with { ResultValue = value, ResultType = value.TypeInfo };

    public abstract VBTypedValue Evaluate(VBExecutionContext context);
}