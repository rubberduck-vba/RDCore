using Microsoft.Extensions.Options;
using System.Text;

namespace RDCore.SDK.Services.VerboseMessages;

public record class ExpressionInfoFormatterProvider(IOptionsFactory<VerboseMessageOptions> Options) : IExpressionInfoFormatterProvider
{
    public IExpressionInfoFormatter CreateNew(StringBuilder builder) => new ExpressionInfoBuilder(Options.Create("default"));
}
