using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.SDK.Model.Expressions.Abstract;

namespace RDCore.SDK.Model.Expressions;

/// <summary>
/// Represents any expression that can be statically evaluated to a <c>VBType</c>, and with runtime semantics to a <c>VBValue</c>.
/// </summary>
public abstract record class ValuedExpression(Location Location) : BoundExpression(Location)  { }
