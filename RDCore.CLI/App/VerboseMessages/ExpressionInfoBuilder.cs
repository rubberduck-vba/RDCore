using RDCore.SDK.Model.Errors;
using System.Text;

namespace RDCore.CLI.App.VerboseMessages;

public record class ExpressionInfoBuilder: IExpressionInfoFormatter
{
    private readonly VerboseMessageOptions _options;

    public ExpressionInfoBuilder(VerboseMessageOptions options)
    {
        _options = options;
    }

    public void Format(StringBuilder builder, VBErrorExpression expression)
    {
        FormatExpressionType(builder, expression);
        FormatSemanticId(builder, expression);
        FormatLocation(builder, expression);
    }

    protected virtual void FormatExpressionType(StringBuilder builder, VBErrorExpression expression)
        => builder
            .WithEnclosedIf(_options.ExpressionType, "[", sb => sb.Append(expression.Kind), "] ");

    protected virtual void FormatSemanticId(StringBuilder builder, VBErrorExpression expression)
        => builder
            .WithIconIf(_options.ExpressionSemanticId && _options.ShowSemanticIdIcon, _options.SemanticIdIcon)
            .WithConditional(_options.ExpressionSemanticId, expression.SemanticId);

    protected virtual string FormatUri(Uri uri) => uri.ToString(); // TODO UriFormatter, discrimate by namespace.

    protected virtual void FormatLocation(StringBuilder builder, VBErrorExpression expression)
        => builder
            .WithConditional(_options.DocumentLocation, sb1 => sb1
            .WithIconIf(_options.ShowDocumentLocationIcon, _options.DocumentLocationIcon)
            .Append('L').Append(expression.Location.Range.Start.Line)
            .Append('C').Append(expression.Location.Range.Start.Character)
            .WithConditional(!expression.Location.Range.IsEmpty, sb2 => sb2.Append(" ..")
                .Append('L').Append(expression.Location.Range.End.Line)
                .Append('C').Append(expression.Location.Range.End.Character)));
}
