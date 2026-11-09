using System.Text;

namespace RDCore.CLI.App.VerboseMessages;

public interface IExpressionInfoFormatterProvider
{
    IExpressionInfoFormatter CreateNew(StringBuilder builder);
}
