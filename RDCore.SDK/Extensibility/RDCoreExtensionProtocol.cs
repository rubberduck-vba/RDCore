namespace RDCore.SDK.Extensibility;

/// <summary>
/// The names of the methods of the RDCore platform's own protocol (<c>rdcore/*</c>) that the language server sends an extension: the extension contract on
/// the wire.
/// </summary>
/// <remarks>
/// An extension serves a method when it provides the capability of the same name (<c>[assembly: ProvidesCorePlatformClientCapability&lt;T&gt;]</c>), and
/// the language server sends it only to an extension that does.
/// </remarks>
public static class RDCoreExtensionProtocol
{
    /// <summary>
    /// Hands a diagnostics-provider extension a parsed document and asks for the diagnostics it finds.
    /// </summary>
    public const string DiagnoseDocument = "rdcore/diagnostics/document";
}
