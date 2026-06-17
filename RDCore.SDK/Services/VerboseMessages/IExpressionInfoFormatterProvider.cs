using System.Text;

namespace RDCore.SDK.Services.VerboseMessages;

public interface IExpressionInfoFormatterProvider
{
    IExpressionInfoFormatter CreateNew(StringBuilder builder);
}
