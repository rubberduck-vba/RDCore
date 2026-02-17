using RDCore.Parsing.Model.Types.Abstract;
using RDCore.Parsing.Model.Values;

namespace RDCore.Parsing.Model.Abstract;

internal interface IBoundNode
{
    VBType ResultType { get; init; }
}

internal interface IExecutableNode
{
    VBTypedValue ResultValue { get; init; }
}