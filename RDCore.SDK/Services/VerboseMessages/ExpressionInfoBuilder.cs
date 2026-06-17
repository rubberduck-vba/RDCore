using RDCore.SDK.Model.AST.Abstract;
using System.Text;

namespace RDCore.SDK.Services.VerboseMessages;

public record class ExpressionInfoBuilder: IExpressionInfoFormatter
{
    private readonly VerboseMessageOptions _options;

    public ExpressionInfoBuilder(VerboseMessageOptions options)
    {
        _options = options;
    }

    public void Format(StringBuilder builder, BoundExpression expression)
    {
        FormatExpressionType(builder, expression);
        FormatSemanticId(builder, expression);
        FormatLocation(builder, expression);
    }

    protected virtual void FormatExpressionType(StringBuilder builder, BoundExpression expression) 
        => builder
            .WithEnclosedIf(_options.ExpressionType, "[", sb => sb.Append(expression.GetType().Name), "] ");

    protected virtual void FormatSemanticId(StringBuilder builder, BoundExpression expression) 
        => builder
            .WithIconIf(_options.ExpressionSemanticId && _options.ShowSemanticIdIcon, _options.SemanticIdIcon)
            .WithConditional(_options.ExpressionSemanticId, expression.SemanticId.ToString());

    /// <summary>
    /// Formats a <c>Uri</c>.
    /// </summary>
    /// <remarks>
    /// 🧩 Base implementation simply dumps the <c>Uri.ToString()</c> representation into the output.
    /// </remarks>
    protected virtual string FormatUri(Uri uri) => uri.ToString(); // TODO UriFormatter, discrimate by namespace.

    /// <summary>
    /// Formats the <c>DocumentLocation</c> of the specified expression.
    /// </summary>
    /// <remarks>
    /// 🧩 Base implementation uses <c>&lt;Start&gt;[ ..&lt;End&gt;]</c> <c>L1C1</c> notation 
    /// where the <c>End</c> coordinates only appear if different than the <c>Start</c> coordinates.
    /// </remarks>
    protected virtual void FormatLocation(StringBuilder builder, BoundExpression expression)
        => builder
            .WithConditional(_options.DocumentLocation, sb1 => sb1
            .WithIconIf(_options.ShowDocumentLocationIcon, _options.DocumentLocationIcon)
            .Append('L').Append(expression.Location.Range.Start.Line)
            .Append('C').Append(expression.Location.Range.Start.Character)
            .WithConditional(!expression.Location.Range.IsEmpty(), sb2 => sb2.Append(" ..")
                .Append('L').Append(expression.Location.Range.End.Line)
                .Append('C').Append(expression.Location.Range.End.Character)));
}
