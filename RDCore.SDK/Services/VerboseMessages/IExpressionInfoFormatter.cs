using RDCore.SDK.Model.AST.Abstract;
using System.Text;

namespace RDCore.SDK.Services.Formatters;

public interface IExpressionInfoFormatter
{
    void Format(StringBuilder builder, BoundExpressionNode expression);
}
