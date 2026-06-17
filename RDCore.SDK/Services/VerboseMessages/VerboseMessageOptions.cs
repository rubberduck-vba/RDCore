namespace RDCore.SDK.Services.VerboseMessages;

/// <summary>
/// Configures the content of <c>Verbose</c> messages.
/// </summary>
/// <param name="IsEnabled"><c>true</c> if <em>verbose</em> messages are enabled, <c>false</c> otherwise.</param>
/// <param name="ShowMessageIcon"><c>true</c> if <em>verbose</em> message should include an icon (string/emoji prefix) for its <c>Message</c> part.</param>
/// <param name="MessageIcon">A string (emoji) that prefixes the <c>Message</c> part of the completed <em>verbose</em> string, e.g. "💠".</param>
/// <param name="ExpressionType"><c>true</c> if <em>verbose</em> messages should include details about the internal type of expression.</param>
/// <param name="ExpressionSemanticId"><c>true</c> if <em>verbose</em> messages should include the <c>SemanticId</c> (a <c>Uri</c>) of the expression.</param>
/// <param name="ShowSemanticIdIcon"><c>true</c> if <em>verbose</em> message should include an icon (string/emoji prefix) for its <c>SemanticId</c> part.</param>
/// <param name="SemanticIdIcon">A string (emoji) that prefixes the <c>SemanticId</c> part, e.g. "🔑".</param>
/// <param name="DocumentLocation"><c>true</c> if <em>verbose</em> messages should format the <em>document location</em> (<c>L1C1</c> range coordinates) of the expression.</param>
/// <param name="ShowDocumentLocationIcon"><c>true</c> if <em>verbose</em> message should include an icon (string/emoji prefix) for its <c>DocumentLocation</c> part, e.g. "📌".</param>
/// <param name="DocumentLocationIcon">A string (emoji) that prefixes the <c>DocumentLocation</c> part.</param>
/// <param name="StackTrace"><c>true</c> if <em>verbose</em> messages should include the <em>stack trace</em> of the operation.</param>
/// <param name="ShowStackTraceIcon"><c>true</c> if <em>verbose</em> message should include an icon (string/emoji prefix) for its <c>StackTrace</c> part.</param>
/// <param name="StackTraceIcon">A string (emoji) that prefixes the <c>StackTraceIcon</c> part, e.g. "👉".</param>
/// <param name="StackFrameMarker">A marker that prefixes each <em>frame</em> (line) of a <em>stack trace</em>, e.g. "&gt;".</param>
/// <param name="TopStackFrameMarker">A marker that prefixes the top-most (current) <em>frame</em> of a <em>stack trace</em>, e.g. "&gt;💥".</param>
/// <param name="ShowValues"><c>true</c> if <em>stack traces</em> should include the <em>input values</em> of stack frames.</param>
/// <param name="ShowValuesIcon"><c>true</c> if <em>verbose</em> message should include an icon (string/emoji prefix) highlighting the inputs (or their data types, if values are not shown) part of the top-most frame, e.g. "🚩".</param>
/// <param name="ValuesIcon">A string (emoji) that prefixes the <c>Values</c> part, e.g. "🚩".</param>
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
    string ValuesIcon = "🚩");
