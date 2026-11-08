using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using RDCore.LanguageServer.Diagnostics;
using RDCore.SDK.Extensibility;

namespace RDCore.Tests.LanguageServer;

[TestClass]
public sealed class ExtensionFindingsTests
{
    private static ExtensionInfo Extension(string title, string? prefix, bool core = false) => new()
    {
        Name = $"{title}.exe",
        Title = title,
        Version = new Version(1, 0),
        Publisher = "",
        PublisherWebUrl = "",
        Description = "",
        FileHash = "",
        Capabilities = [],
        DiagnosticPrefix = prefix,
        IsCoreExtension = core,
    };

    private static Diagnostic Finding(string code, string? help = null) => new()
    {
        Code = new DiagnosticCode(code),
        Source = "RDCore",
        Message = "message",
        CodeDescription = help is null ? null : new CodeDescription { Href = new Uri(help) },
    };

    private const string PlatformHelp = "https://rubberduck-vba.github.io/RDCore/diagnostics/xyz00042.html";

    [TestMethod]
    public void TheCoreExtensionsFindings_AreReportedAsTheyAre()
    {
        IReadOnlyList<Diagnostic> found = [Finding("VBC01027", PlatformHelp), Finding("RDC00101"), Finding("XYZ00042")];

        var admitted = ExtensionFindings.Admit(Extension("RDCore.Diagnostics", "RDC", core: true), found, NullLogger.Instance);

        Assert.AreSame(found, admitted);
    }

    [TestMethod]
    public void AnotherExtensionsFinding_OfItsPrefix_IsItsOwn_AndNotDescribedByThePlatformsDocumentation()
    {
        var admitted = Assert.ContainsSingle(ExtensionFindings.Admit(Extension("Vendor", "XYZ"), [Finding("XYZ00042", PlatformHelp)], NullLogger.Instance));

        Assert.AreEqual("Vendor", admitted.Source);
        Assert.AreEqual("XYZ00042", admitted.Code!.Value.String);
        Assert.IsNull(admitted.CodeDescription);
    }

    [TestMethod]
    public void AnotherExtensionsFinding_KeepsTheHelpOfItsPublisher()
    {
        var admitted = Assert.ContainsSingle(ExtensionFindings.Admit(Extension("Vendor", "XYZ"), [Finding("XYZ00042", "https://example.test/xyz00042")], NullLogger.Instance));

        Assert.AreEqual("https://example.test/xyz00042", admitted.CodeDescription!.Href.ToString());
    }

    [TestMethod]
    public void AnotherExtensionsFindings_OfAnotherCode_AreDropped_AndLogged()
    {
        var logger = new RecordingLogger();
        Diagnostic numbered = new() { Code = new DiagnosticCode(42), Message = "message" };

        var admitted = ExtensionFindings.Admit(Extension("Vendor", "XYZ"), [Finding("RDC00101"), Finding("VBC01027"), Finding("QRS00001"), Finding("XYZ42"), numbered, Finding("XYZ00042")], logger);

        Assert.AreEqual("XYZ00042", Assert.ContainsSingle(admitted).Code!.Value.String);
        var warning = Assert.ContainsSingle(logger.Entries.Where(entry => entry.Level == LogLevel.Warning));
        StringAssert.Contains(warning.Message, "5 finding(s) of extension 'Vendor'");
        StringAssert.Contains(warning.Message, "RDC00101");
    }

    [TestMethod]
    public void AnExtensionThatDeclaresNoPrefix_HasNoFindingReported()
        => Assert.IsEmpty(ExtensionFindings.Admit(Extension("Vendor", null), [Finding("XYZ00042")], NullLogger.Instance));

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
