using RDCore.SDK.Model.Errors;
using System.Text;

namespace RDCore.CLI.App.VerboseMessages;

public class LetCoercionTraceFormatter : DefaultStackTraceFormatter
{
    private readonly VerboseMessageOptions _options;

    public LetCoercionTraceFormatter(VerboseMessageOptions options)
        : base(options)
    {
        _options = options;
    }

    // a frame that is no let-coercion is formatted as any other.
    protected override void FormatOperands(StringBuilder builder, VBErrorFrame frame, bool withValues = false)
    {
        if (frame.Coercion is { } coercion)
        {
            FormatOperands(builder, coercion, withValues);
        }
        else
        {
            base.FormatOperands(builder, frame, withValues);
        }
    }

    private static void FormatOperands(StringBuilder builder, VBErrorCoercion coercion, bool withValues = false) =>
        builder
            .WithEnclosedIf(withValues, "[", coercion.Source.Value ?? "🦄", "]:")
            .Append(coercion.Source.TypeName)
            .Append(" -> ")
            .Append(coercion.DestinationTypeName);
}
