using RDCore.SDK.Model.Errors;
using System.Text;

namespace RDCore.CLI.App.VerboseMessages;

public interface IExpressionInfoFormatter
{
    void Format(StringBuilder builder, VBErrorExpression expression);
}
