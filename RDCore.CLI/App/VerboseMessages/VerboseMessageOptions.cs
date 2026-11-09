namespace RDCore.CLI.App.VerboseMessages;

public record class VerboseMessageOptions(
    bool IsEnabled = true,
    bool ShowMessageIcon = true,
    string MessageIcon = "💠",
    bool ExpressionType = true,
    bool ExpressionSemanticId = true,
    bool ShowSemanticIdIcon = true,
    string SemanticIdIcon = "🔑",
    bool DocumentLocation = true,
    bool ShowDocumentLocationIcon = true,
    string DocumentLocationIcon = "📌",
    bool StackTrace = true,
    bool ShowStackTraceIcon = true,
    string StackTraceIcon = "👉",
    string StackFrameMarker = ">",
    string TopStackFrameMarker = ">💥",
    bool ShowValues = false,
    bool ShowValuesIcon = true,
    string ValuesIcon = "🚩")
{
    public VerboseMessageOptions() : this(IsEnabled: true) { }
}
