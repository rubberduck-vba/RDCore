using RDCore.External.Automation;
using RDCore.External.Forms.RDForms;
using RDCore.Forms.Model;
using System.Globalization;

namespace RDCore.Tests.External;

/// <summary>
/// The fonts the Font properties of RDForms components hand out: what a change through one makes each component that holds it draw.
/// </summary>
[TestClass]
public sealed class RDFormsFontsTests
{
    private static ComponentInstance Label(string name) => new(ComponentClasses.Label, name);

    private static void Assign(RDFormsFont font, string member, object value)
        => _ = RDFormsFontMembers.Invoke(font, member, AutomationInvocation.Let, [value], CultureInfo.InvariantCulture);

    [TestMethod]
    public void AChangeThroughTheFont_IsWhatTheComponentDraws()
    {
        var label = Label("Label1");
        var fonts = new RDFormsFonts();
        var font = fonts.FontOf(label, Properties.Font);

        Assign(font, "Bold", true);
        Assign(font, "Name", "Arial");
        Assign(font, "Size", 14m);

        var drawn = label.Get(Properties.Font);
        Assert.AreEqual(("Arial", 14.25, FormFont.BoldWeight), (drawn.Name, drawn.Size, drawn.Weight));
        Assert.AreSame(font, fonts.FontOf(label, Properties.Font));
    }

    [TestMethod]
    public void AFontShared_IsDrawnByEachComponent_AndTheOneLetGoOfChangesNoMore()
    {
        var first = Label("Label1");
        var second = Label("Label2");
        var fonts = new RDFormsFonts();
        var own = fonts.FontOf(second, Properties.Font);
        var shared = fonts.FontOf(first, Properties.Font);

        fonts.Share(second, Properties.Font, shared);
        Assign(shared, "Italic", true);
        Assign(own, "Underline", true);

        Assert.IsTrue(first.Get(Properties.Font).Italic);
        Assert.IsTrue(second.Get(Properties.Font).Italic);
        Assert.IsFalse(second.Get(Properties.Font).Underline);
        Assert.AreSame(shared, fonts.FontOf(second, Properties.Font));
    }
}
