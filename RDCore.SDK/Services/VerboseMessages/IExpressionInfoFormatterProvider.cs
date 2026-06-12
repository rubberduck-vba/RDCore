using System.Text;

namespace RDCore.SDK.Services.Formatters
{
    public interface IExpressionInfoFormatterProvider
    {
        IExpressionInfoFormatter CreateNew(StringBuilder builder);
    }
}
