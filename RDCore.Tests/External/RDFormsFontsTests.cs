using NSubstitute;
using RDCore.External.Automation;
using RDCore.External.Forms.RDForms;
using RDCore.Forms.Model;
using RDCore.SDK.Workspace.Forms;
using System.Globalization;

namespace RDCore.Tests.External;

/// <summary>
/// The fonts the Font properties of RDForms components hand out: what a change through one makes each component that holds it draw. A font of the program's
/// (<c>New StdFont</c>) is the session's: the components draw the values it crossed with, and those it holds once it changed, and its members are the
/// session's.
/// </summary>
[TestClass]
public sealed class RDFormsFontsTests
{
    private const string Program = "font 7";

    private static readonly SessionFont Arial = new("Arial", IsNamed: true, 12m, 700, 0, Italic: false, Underline: false, Strikethrough: false);

    private static ComponentInstance Label(string name) => new(ComponentClasses.Label, name);

    private static void Assign(RDFormsFont font, string member, object value)
        => _ = RDFormsFontMembers.Invoke(font, member, AutomationInvocation.Let, [value], CultureInfo.InvariantCulture);

    private static SessionObjectReference ProgramFont(ISessionObjectHost host) => new(Program, host) { Font = Arial };

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

    [TestMethod]
    public void AFontOfTheProgramsSetToTwoComponents_IsOneFont_ThatHoldsOneCrossing_AndIsDrawnByBothAsItChanges()
    {
        var host = Substitute.For<ISessionObjectHost>();
        var first = Label("Label1");
        var second = Label("Label2");
        var fonts = new RDFormsFonts();

        fonts.Share(first, Properties.Font, ProgramFont(host), Arial);
        fonts.Share(second, Properties.Font, ProgramFont(host), Arial);
        fonts.Changed(ProgramFont(host) with { Font = Arial with { Italic = true } });

        var font = fonts.FontOf(first, Properties.Font);
        Assert.AreSame(font, fonts.FontOf(second, Properties.Font));
        Assert.AreEqual(Program, font.Program?.SessionObject);
        Assert.AreEqual(("Arial", 12d, FormFont.BoldWeight, true), (first.Get(Properties.Font).Name, first.Get(Properties.Font).Size,
            first.Get(Properties.Font).Weight, first.Get(Properties.Font).Italic));
        Assert.IsTrue(second.Get(Properties.Font).Italic);
        host.Received(1).Release(Program, 1);
    }

    [TestMethod]
    public void AFontOfTheProgramsThatNoComponentHolds_OrWhoseFormIsGone_IsLetGoOf_AndDrawnNoMore()
    {
        var host = Substitute.For<ISessionObjectHost>();
        var first = Label("Label1");
        var second = Label("Label2");
        var fonts = new RDFormsFonts();

        fonts.Share(first, Properties.Font, ProgramFont(host), Arial);
        fonts.Share(first, Properties.Font, fonts.FontOf(second, Properties.Font));
        host.Received(1).Release(Program, 1);

        fonts.Share(second, Properties.Font, ProgramFont(host), Arial);
        fonts.ReleaseAll();
        host.Received(2).Release(Program, 1);

        fonts.Changed(ProgramFont(host) with { Font = Arial with { Underline = true } });
        Assert.IsFalse(second.Get(Properties.Font).Underline);
    }

    [TestMethod]
    public void AMemberOfAFontOfTheProgramsIs_TheSessionFontsMember()
    {
        var host = Substitute.For<ISessionObjectHost>();
        host.Invoke(Arg.Is<SessionObjectCall>(call => call.Invocation == AutomationInvocation.Get)).Returns("Arial");
        var label = Label("Label1");
        var fonts = new RDFormsFonts();
        fonts.Share(label, Properties.Font, ProgramFont(host), Arial);
        var font = fonts.FontOf(label, Properties.Font);

        Assign(font, "Italic", true);
        var name = RDFormsFontMembers.Invoke(font, "[DISPID=0]", AutomationInvocation.Get, [], CultureInfo.InvariantCulture);

        host.Received(1).Invoke(Arg.Is<SessionObjectCall>(call => call.SessionObject == Program && call.Name == "Italic"
            && call.Invocation == AutomationInvocation.Let && call.Arguments.SequenceEqual(new object?[] { true }) && call.IsSynchronous));
        Assert.AreEqual("Arial", name);
        Assert.IsFalse(label.Get(Properties.Font).Italic, "what is drawn is what the session says the font holds once it changed");
    }

    // Form1, with Text1, a TextBox in the form's font.
    private const string Designer =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   ClientHeight    =   3195\r\n" +
        "   ClientWidth     =   4680\r\n" +
        "   Begin VB.TextBox Text1 \r\n" +
        "      Height          =   285\r\n" +
        "      Left            =   120\r\n" +
        "      TabIndex        =   0\r\n" +
        "      Text            =   \"Text1\"\r\n" +
        "      Top             =   120\r\n" +
        "      Width           =   1215\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    [TestMethod]
    public void AChangeToldToTheServer_IsDrawnByTheFormsControl_WhichReadsBackTheProgramsFont_UntilTheFormIsGone()
    {
        var host = Substitute.For<ISessionObjectHost>();
        var forms = new RDFormsFormHost(new RDFormsAutomationServer(RDFormsPlatform.Headless));
        var form = forms.Create(new RDCore.External.Forms.FormDefinition("Form1", ReadOnlyMemory<byte>.Empty)
        {
            Kind = FormDesignerKind.VisualBasic6,
            Caption = "Form1",
            DesignerText = Designer,
        });
        object? read;
        FormFont drawn;
        try
        {
            var text = forms.Server.Invoke(form, "Text1", AutomationInvocation.Get, [], [], CultureInfo.InvariantCulture)!;
            _ = forms.Server.Invoke(text, "Font", AutomationInvocation.Set, [ProgramFont(host)], [false], CultureInfo.InvariantCulture);

            forms.Server.SessionObjectChanged(ProgramFont(host) with { Font = Arial with { Italic = true } });

            read = forms.Server.Invoke(text, "Font", AutomationInvocation.Get, [], [], CultureInfo.InvariantCulture);
            drawn = RDFormsUI.Run(forms.Server.Program, () => RDFormsAutomationServer.HandleOf(text).Component.Get(Properties.Font));
            host.DidNotReceive().Release(Program, Arg.Any<int>());
        }
        finally
        {
            forms.Destroy(form);
        }

        Assert.AreEqual(Program, (read as SessionObjectReference)?.SessionObject);
        Assert.AreEqual(("Arial", true), (drawn.Name, drawn.Italic));
        host.Received(1).Release(Program, 1);
    }
}
