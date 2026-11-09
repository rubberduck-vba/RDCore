using System.Globalization;
using System.Resources;
using RDCore.CLI;
using RDCore.LanguageServer.Debugging;

namespace RDCore.Tests.LanguageServer;

/// <summary>
/// What a debugger client is shown comes from resources, and each of them has its French.
/// </summary>
[TestClass]
public sealed class DebuggerMessagesTests
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr");

    private static string[] Keys(ResourceManager manager, CultureInfo culture)
    {
        var set = manager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return [.. set.Cast<System.Collections.DictionaryEntry>().Select(entry => (string)entry.Key).Order()];
    }

    [TestMethod]
    public void TheMessagesOfTheDebugAdapter_AreAllTranslated()
    {
        var neutral = Keys(DebuggerMessages.ResourceManager, CultureInfo.InvariantCulture);
        var french = Keys(DebuggerMessages.ResourceManager, French);

        Assert.IsNotEmpty(neutral);
        CollectionAssert.AreEqual(neutral, french);
    }

    [TestMethod]
    public void TheMessagesOfTheHost_AreAllTranslated()
    {
        var neutral = Keys(Resources.ResourceManager, CultureInfo.InvariantCulture).Where(key => key.StartsWith("Host_")).ToArray();
        var french = Keys(Resources.ResourceManager, French).Where(key => key.StartsWith("Host_")).ToArray();

        Assert.IsNotEmpty(neutral);
        CollectionAssert.AreEqual(neutral, french);
    }

    [TestMethod]
    public void AMessage_IsInTheLanguageOfTheCulture()
    {
        Assert.AreEqual("Main", DebuggerMessages.ResourceManager.GetString("ThreadMain", CultureInfo.InvariantCulture));
        Assert.AreEqual("Principal", DebuggerMessages.ResourceManager.GetString("ThreadMain", French));
        Assert.AreEqual("le programme est en cours d'exécution",Resources.ResourceManager.GetString("Host_TheProgramIsRunning", French));
    }
}
