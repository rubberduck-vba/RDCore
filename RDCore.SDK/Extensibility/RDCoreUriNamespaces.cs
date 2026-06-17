namespace RDCore.SDK.Extensibility;

/// <summary>
/// Defines the various URI namespaces used throughout <c>RDCore.SDK</c>.
/// </summary>
public static class RDCoreUriNamespaces
{
    public const string RDCoreBaseUri = "file://uri.rdcore.sdk";

    // these two don't really belong here.. TODO find them a better home:
    public const string RDCoreLanguageCode = "RD-VBA";
    public const string RDCoreLanguageName = "RDCore Visual Basic for Applications";

    /// <summary>
    /// This <c>Uri</c> namespace defines various language-level elements.
    /// </summary>
    public const string RDCoreLangBaseUri = $"{RDCoreBaseUri}/rd-vba";
    /// <summary>
    /// This <c>Uri</c> namespace defines various runtime language-level elements.
    /// </summary>
    public const string RDCoreLanguageSpaceUri = $"{RDCoreBaseUri}/rd-vba/lang";
    /// <summary>
    /// This <c>Uri</c> contains all <em>static</em> (unallocated) symbols.
    /// </summary>
    public const string RDCoreLanguageSpaceGlobalUri = $"{RDCoreBaseUri}/rd-vba/lang/global";

    /// <summary>
    /// The base <c>Uri</c> for all RDCore dianostics.
    /// </summary>
    public const string RDCoreDiagnosticsUri = $"{RDCoreLangBaseUri}/diagnostics";

    /// <summary>
    /// The base <c>Uri</c> for any <c>RDCore</c> workspaces.
    /// </summary>
    /// <remarks>
    /// This is a <c>uri.rdcore.sdk</c> URI that should only be used for <em>virtual workspaces</em>.<br/>
    /// Project workspaces are otherwise absolute URI (always a '<c>file://path</c>' URI).
    /// </remarks>
    public const string RDCoreWorkspaceUri = $"{RDCoreBaseUri}/workspaces";
}
