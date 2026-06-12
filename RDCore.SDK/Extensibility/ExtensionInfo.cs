namespace RDCore.LanguageServer.Extensibility
{
    /// <summary>
    /// The information contained in an <em>extension manifest</em>.
    /// </summary>
    /// <remarks>
    /// 🧩 This is how RDCore recognizes an extension as such.
    /// </remarks>
    /// <param name="Name">The name of the extension executable (.exe).</param>
    /// <param name="Title">The <em>friendly name</em> / title of the extension. This should also be the name of the folder.</param>
    /// <param name="Version">The version of the extension.</param>
    /// <param name="Publisher">The name of the publisher.</param>
    /// <param name="PublisherWebUrl">The publisher's web URL.</param>
    /// <param name="Description">A short description of the extension.</param>
    /// <param name="AppId"> The <em>application ID</em> of a registered extension that authenticates with the RDCore cloud infrastructure, if applicable.</param>
    public record class ExtensionInfo(
        string Name,
        string Title,
        Version Version,
        string Publisher,
        string PublisherWebUrl,
        string Description,
        string? AppId)
    {
    }
}
