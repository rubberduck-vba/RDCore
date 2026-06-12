using Microsoft.Extensions.Options;
using RDCore.SDK.Services.VerboseMessages;
using System.Text;

namespace RDCore.SDK.Services.Formatters
{
    public record class ExpressionInfoFormatterProvider(IOptionsFactory<VerboseMessageOptions> Options) : IExpressionInfoFormatterProvider
    {
        public IExpressionInfoFormatter CreateNew(StringBuilder builder) => new ExpressionInfoBuilder(Options.Create("default"));
    }
}
