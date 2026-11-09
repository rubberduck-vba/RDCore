using RDCore.SDK.Model.Errors;
using System.Text;

namespace RDCore.CLI.App.VerboseMessages;

public interface IStackTraceFormatter
{
    void Format(StringBuilder builder, IEnumerable<VBErrorFrame> stack);
}
