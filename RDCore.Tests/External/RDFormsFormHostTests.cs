using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.External.Forms.RDForms;
using RDCore.SDK.Workspace.Forms;
using System.Globalization;
using System.Reflection;

namespace RDCore.Tests.External;

/// <summary>
/// The RDForms form host and its automation server: a Visual Basic 6.0 form read from its designer's text, in a headless RDForms window, its controls reached by
/// name, their properties read and written, their events raised to the sink a program advises; and how an external host routes forms and objects between RDForms
/// and its platform's own servers.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class RDFormsFormHostTests
{
    private const string Designer =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Designed\"\r\n" +
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

    private sealed class Recorder : IAutomationEventSink
    {
        public List<(string Name, bool IsSynchronous)> Raised { get; } = [];

        public Action<AutomationEvent>? Handler { get; init; }

        public void OnEvent(AutomationEvent raised)
        {
            lock (Raised)
            {
                Raised.Add((raised.Name, raised.IsSynchronous));
            }

            Handler?.Invoke(raised);
        }
    }

    private static RDFormsFormHost Host() => new(new RDFormsAutomationServer(RDFormsPlatform.Headless));

    private static FormDefinition VisualBasicForm(string? designer = Designer)
        => new("Form1", ReadOnlyMemory<byte>.Empty) { Kind = FormDesignerKind.VisualBasic6, Caption = "Form1", DesignerText = designer };

    private static object? Get(RDFormsFormHost host, object target, string member)
        => host.Server.Invoke(target, member, AutomationInvocation.Get, [], [], CultureInfo.InvariantCulture);

    private static void Let(RDFormsFormHost host, object target, string member, object? value)
        => host.Server.Invoke(target, member, AutomationInvocation.Let, [value], [false], CultureInfo.InvariantCulture);

    [TestMethod]
    public void AVisualBasicForm_IsReadFromItsDesignerText_AndItsControlsAreReachedByName()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            var text = Get(host, form, "Text1")!;

            Assert.AreEqual("Designed", host.GetCaption(form));
            Assert.AreEqual("Text1", Get(host, text, "Text"));
            Assert.AreEqual("VB.TextBox", host.Server.ClassNameOf(text));
            Assert.AreEqual("VB.Form", host.Server.ClassNameOf(form));
            Assert.IsTrue(host.Server.IsInstanceOf(text, "VB", "TextBox"));
            Assert.AreSame(text, Get(host, form, "Text1"), "a control asked for twice is one object");
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void AVisualBasicForm_WithNoDesignerText_IsAnEmptyForm_WithTheCaptionItIsGiven()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm(designer: null));
        try
        {
            Assert.AreEqual("Form1", host.GetCaption(form));
            var failure = Assert.ThrowsExactly<AutomationException>(() => Get(host, form, "Text1"));
            Assert.AreEqual(unchecked((int)0x80020006), failure.HResult, "DISP_E_UNKNOWNNAME: error 438");
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void AVisualBasicControlsPositionAndSize_AreInTwips_AsItsDesignerWritesThem()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            var text = Get(host, form, "Text1")!;

            Assert.AreEqual(120d, Get(host, text, "Left"));
            Assert.AreEqual(120d, Get(host, text, "Top"));
            Assert.AreEqual(1215d, Get(host, text, "Width"));
            Assert.AreEqual(285d, Get(host, text, "Height"));

            Let(host, text, "Left", 360);
            Let(host, text, "Width", 2400);
            Assert.AreEqual(360d, Get(host, text, "Left"));
            Assert.AreEqual(2400d, Get(host, text, "Width"));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void AVisualBasicControlsPositionAndSize_AreInItsContainersScale()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            var text = Get(host, form, "Text1")!;
            Let(host, form, "ScaleMode", 3);

            // ScaleMode 3 is pixels, at 15 twips to the pixel.
            Assert.AreEqual(8d, Get(host, text, "Left"));
            Assert.AreEqual(81d, Get(host, text, "Width"));

            Let(host, text, "Left", 16);
            Let(host, form, "ScaleMode", 1);
            Assert.AreEqual(240d, Get(host, text, "Left"));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void AVisualBasicFormsOwnSize_IsItsClientArea_InTwips_WhateverItsScale()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            Let(host, form, "ScaleMode", 3);

            Assert.AreEqual(4680d, Get(host, form, "Width"));
            Assert.AreEqual(3195d, Get(host, form, "Height"));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void AFormsCaption_IsItsOwnCaptionProperty()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            host.SetCaption(form, "Set by the host");
            Assert.AreEqual("Set by the host", Get(host, form, "Caption"));

            Let(host, form, "Caption", "Set by the program");
            Assert.AreEqual("Set by the program", host.GetCaption(form));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void AssigningAControlsProperty_RaisesItsEvent_InsideTheCall()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            host.Show(form, modal: false);
            var text = Get(host, form, "Text1")!;
            var sink = new Recorder();
            host.Server.Advise(text, sink);

            Let(host, text, "Text", "typed");

            Assert.AreEqual("typed", Get(host, text, "Text"));
            Assert.AreEqual(("Change", true), Assert.ContainsSingle(sink.Raised), "raised by the program's own call, the event is its answer");
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void AValueAPropertyCannotTake_IsError380_AndOneOfTheWrongType_Error13()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            var text = Get(host, form, "Text1")!;

            var invalid = Assert.ThrowsExactly<AutomationException>(() => Let(host, text, "Alignment", 7));
            var mismatch = Assert.ThrowsExactly<AutomationException>(() => Let(host, text, "Width", "wide"));

            Assert.AreEqual(unchecked((int)0x800A017C), invalid.HResult, "error 380");
            Assert.AreEqual(unchecked((int)0x80020005), mismatch.HResult, "DISP_E_TYPEMISMATCH: error 13");
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public async Task AFormShownModal_WaitsUntilItIsHidden()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            // the window raises Resize inside its first Show, before the wait begins: the hide asked for once it has is made while the form waits.
            using var shown = new ManualResetEventSlim();
            host.Server.Advise(form, new Recorder { Handler = raised => { if (raised.Name == "Resize") { shown.Set(); } } });

            var showing = Task.Run(() => host.Show(form, modal: true));
            shown.Wait();
            Assert.IsTrue(host.IsShown(form), "asked while the form waits");
            host.Hide(form);
            await showing;

            Assert.IsFalse(host.IsShown(form));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void AFormHiddenByAHandlerOfItsFirstResize_IsNotWaitedFor()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            host.Server.Advise(form, new Recorder { Handler = raised => { if (raised.Name == "Resize") { host.Hide(form); } } });

            host.Show(form, modal: true);

            Assert.IsFalse(host.IsShown(form));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    // the keyboard's focus is the UI thread's, which every RDForms window shares: a row that types runs alone, with no other test's window activated meanwhile.
    [TestMethod]
    [DoNotParallelize]
    [DataRow((short)'a', "a", DisplayName = "the character as it was typed is typed")]
    [DataRow((short)'B', "B", DisplayName = "another character is typed instead")]
    [DataRow((short)0, "", DisplayName = "0 types nothing")]
    public void WhatTheSinkLeavesInKeyAscii_IsWhatTheTextBoxTypes(short left, string typed)
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            // the argument the sink writes to is the one the control reads back, as a KeyPress handler's ByRef KeyAscii.
            var text = Get(host, form, "Text1")!;
            Let(host, text, "Text", string.Empty);
            host.Server.Advise(text, new Recorder { Handler = raised => { if (raised.Name == "KeyPress") { raised.Arguments[0] = left; } } });
            host.Show(form, modal: false);

            // the key goes to the control that has the focus: the TextBox's own control in the window, focused once the window's activation has given the form its
            // focus, as the user's click would after it.
            var control = RDFormsAutomationServer.HandleOf(text);
            var window = control.Form.Window;
            var focused = RDFormsUI.Run(() =>
            {
                window.Activate();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var box = window.View.ControlOf(control.Component)!;
                box.Focus();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                if (!box.IsFocused)
                {
                    return false;
                }

                Avalonia.Headless.HeadlessWindowExtensions.KeyTextInput(window, "a");
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return true;
            });

            Assert.IsTrue(focused, "the TextBox has the focus the key goes to");
            Assert.AreEqual(typed, Get(host, text, "Text"));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    [DataRow(true, false, true, DisplayName = "an owner form that is shown owns it")]
    [DataRow(false, false, false, DisplayName = "an owner form that is not shown does not")]
    [DataRow(true, true, false, DisplayName = "a form shown already keeps the owner it was shown with")]
    public void TheOwnerFormOfAShow_OwnsTheFormsWindow(bool ownerShown, bool shownAlready, bool owned)
    {
        var host = Host();
        var owner = host.Create(VisualBasicForm());
        var form = host.Create(VisualBasicForm());
        try
        {
            if (ownerShown)
            {
                host.Show(owner, modal: false);
            }

            if (shownAlready)
            {
                host.Show(form, modal: false);
            }

            host.Show(form, modal: false, owner);

            var ownerWindow = RDFormsAutomationServer.HandleOf(owner).Form.Window;
            var actual = RDFormsUI.Run(() => RDFormsAutomationServer.HandleOf(form).Form.Window.Owner);
            Assert.AreEqual(owned, ReferenceEquals(ownerWindow, actual));
            Assert.IsTrue(owned || actual is null, "a form not owned has no owner");
        }
        finally
        {
            host.Destroy(form);
            host.Destroy(owner);
        }
    }

    [TestMethod]
    public void ADestroyedForm_IsNoLongerAnObject()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        var text = Get(host, form, "Text1")!;

        host.Destroy(form);
        host.Destroy(form);

        Assert.ThrowsExactly<AutomationException>(() => Get(host, text, "Text"));
        Assert.ThrowsExactly<AutomationException>(() => host.Show(form, modal: false));
    }

    [TestMethod]
    public void AFormLetGoOfByItsServer_IsDestroyed_AsAnExternalHostsRemoteFormHostDestroysIt()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        host.Show(form, modal: false);

        host.Server.Release(form);

        Assert.ThrowsExactly<AutomationException>(() => host.IsShown(form));
    }

    // a form that keeps a hidden top-level menu to pop up: mnuEdit holds Cut and Copy.
    private const string MenuDesigner =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Menus\"\r\n" +
        "   ClientHeight    =   3195\r\n" +
        "   ClientWidth     =   4680\r\n" +
        "   Begin VB.Menu mnuEdit \r\n" +
        "      Caption         =   \"&Edit\"\r\n" +
        "      Visible         =   0   'False\r\n" +
        "      Begin VB.Menu mnuCut \r\n" +
        "         Caption         =   \"Cu&t\"\r\n" +
        "      End\r\n" +
        "      Begin VB.Menu mnuCopy \r\n" +
        "         Caption         =   \"&Copy\"\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    // a form whose hidden menu mnuFile holds the menu array mnuRecent, of indexes 0 and 2.
    private const string MenuArrayDesigner =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Menus\"\r\n" +
        "   ClientHeight    =   3195\r\n" +
        "   ClientWidth     =   4680\r\n" +
        "   Begin VB.Menu mnuFile \r\n" +
        "      Caption         =   \"&File\"\r\n" +
        "      Visible         =   0   'False\r\n" +
        "      Begin VB.Menu mnuRecent \r\n" +
        "         Caption         =   \"First\"\r\n" +
        "         Index           =   0\r\n" +
        "      End\r\n" +
        "      Begin VB.Menu mnuRecent \r\n" +
        "         Caption         =   \"Third\"\r\n" +
        "         Index           =   2\r\n" +
        "      End\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    [TestMethod]
    [DoNotParallelize]
    public void AnElementOfAMenuArray_RaisesItsClick_ToTheArray_WithItsIndexFirst()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm(MenuArrayDesigner));
        try
        {
            var file = Get(host, form, "mnuFile")!;
            var raised = new List<string>();
            host.Server.Advise(Get(host, form, "mnuRecent")!, new Recorder { Handler = clicked => raised.Add($"mnuRecent {clicked.Name} {string.Join(",", clicked.Arguments)}") });
            host.Server.Advise(file, new Recorder
            {
                Handler = _ => OnceItPopsUp(RDFormsAutomationServer.HandleOf(form), popup =>
                {
                    Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(popup).OfType<Avalonia.Controls.MenuItem>()
                        .Single(item => RDCore.Forms.Avalonia.Controls.VBControl.GetInstance(item) is { Name: "mnuRecent", Index: 2 })
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
                    popup.Close();
                }),
            });
            host.Show(form, modal: false);

            PopupMenu(host, form, file);

            CollectionAssert.AreEqual(new[] { "mnuRecent Click 2" }, raised, string.Join(" | ", raised));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    // what a user does with the menu PopupMenu pops up, once it is open: run on the UI thread, after the menu's own Click, while the PopupMenu call waits. The
    // popup opens on a job of its own, so the action waits behind the jobs before it until it is there.
    private static void OnceItPopsUp(RDFormsObject form, Action<Avalonia.Controls.ContextMenu> act, int jobsLeft = 20)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (form.Form.Window.PoppedUpMenu is { IsOpen: true } popup)
            {
                act(popup);
            }
            else if (jobsLeft > 0)
            {
                OnceItPopsUp(form, act, jobsLeft - 1);
            }
            else
            {
                // nothing popped up: hiding the form ends the call, and the row fails on what was raised.
                form.Form.Hide();
            }
        }, Avalonia.Threading.DispatcherPriority.Background);

    private static Avalonia.Controls.MenuItem ItemOf(Avalonia.Controls.ContextMenu popup, string name)
        => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(popup).OfType<Avalonia.Controls.MenuItem>()
            .Single(item => RDCore.Forms.Avalonia.Controls.VBControl.GetInstance(item)?.Name == name);

    private static void PopupMenu(RDFormsFormHost host, object form, params object?[] arguments)
        => host.Server.Invoke(form, "PopupMenu", AutomationInvocation.Method, arguments, new bool[arguments.Length], CultureInfo.InvariantCulture);

    [TestMethod]
    [DoNotParallelize]
    public void PopupMenu_RaisesTheMenusClickAsItOpens_AndReturnsOnceItCloses()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm(MenuDesigner));
        try
        {
            var edit = Get(host, form, "mnuEdit")!;
            var raised = new List<string>();
            host.Server.Advise(edit, new Recorder
            {
                Handler = clicked =>
                {
                    raised.Add($"mnuEdit {clicked.Name}");
                    OnceItPopsUp(RDFormsAutomationServer.HandleOf(form), popup =>
                    {
                        raised.Add("popped up");
                        popup.Close();
                    });
                },
            });
            host.Show(form, modal: false);

            PopupMenu(host, form, edit);
            raised.Add("returned");

            CollectionAssert.AreEqual(new[] { "mnuEdit Click", "popped up", "returned" }, raised, string.Join(" | ", raised));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    [DoNotParallelize]
    public void AnItemChosenFromThePopup_RaisesItsClick_BeforePopupMenuReturns()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm(MenuDesigner));
        try
        {
            var edit = Get(host, form, "mnuEdit")!;
            var raised = new List<string>();
            host.Server.Advise(Get(host, form, "mnuCopy")!, new Recorder { Handler = clicked => raised.Add($"mnuCopy {clicked.Name}") });
            host.Server.Advise(edit, new Recorder
            {
                Handler = clicked =>
                {
                    raised.Add($"mnuEdit {clicked.Name}");
                    OnceItPopsUp(RDFormsAutomationServer.HandleOf(form), popup =>
                    {
                        // a click on the item raises its Click, then the menu closes.
                        ItemOf(popup, "mnuCopy").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.MenuItem.ClickEvent));
                        popup.Close();
                    });
                },
            });
            host.Show(form, modal: false);

            PopupMenu(host, form, edit, 0, Missing.Value, Missing.Value, Get(host, form, "mnuCopy"));
            raised.Add("returned");

            CollectionAssert.AreEqual(new[] { "mnuEdit Click", "mnuCopy Click", "returned" }, raised, string.Join(" | ", raised));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void PopupMenu_IsAFormsMethod_AndNotAControls()
    {
        var host = Host();
        var form = host.Create(VisualBasicForm(MenuDesigner));
        try
        {
            var edit = Get(host, form, "mnuEdit")!;

            Assert.ThrowsExactly<AutomationException>(() => PopupMenu(host, edit, edit));
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    public void AUserForm_IsNotRDFormsToHost()
    {
        var failure = Assert.ThrowsExactly<AutomationException>(() => Host().Create(new FormDefinition("UserForm1", ReadOnlyMemory<byte>.Empty)));

        Assert.AreEqual(unchecked((int)0x80040154), failure.HResult, "REGDB_E_CLASSNOTREG: error 429");
    }

    [TestMethod]
    public void AnExternalHost_HostsVisualBasicFormsWithRDForms_AndUserFormsWithItsPlatform()
    {
        var (automation, forms) = RDFormsHosting.Compose(UnavailableAutomationServer.Instance, UnavailableFormHost.Instance, new FormsOptions { Headless = true });

        var form = forms.Create(VisualBasicForm());
        try
        {
            Assert.AreEqual("Text1", automation.Invoke(
                automation.Invoke(form, "Text1", AutomationInvocation.Get, [], [], CultureInfo.InvariantCulture)!, "Text", AutomationInvocation.Get, [], [],
                CultureInfo.InvariantCulture));
            Assert.AreEqual("Designed", forms.GetCaption(form));
            Assert.IsTrue(automation.IsAvailable, "RDForms' objects are there to reach, with no automation server on the platform");

            var userForm = Assert.ThrowsExactly<AutomationException>(() => forms.Create(new FormDefinition("UserForm1", ReadOnlyMemory<byte>.Empty)));
            var created = Assert.ThrowsExactly<AutomationException>(() => automation.CreateObject("Scripting.Dictionary"));
            Assert.AreEqual(unchecked((int)0x80040154), userForm.HResult);
            Assert.AreEqual(unchecked((int)0x80040154), created.HResult);
        }
        finally
        {
            forms.Destroy(form);
        }
    }

    [TestMethod]
    public void AnExternalHost_WithRDFormsTurnedOff_HostsNoVisualBasicForm()
    {
        var (_, forms) = RDFormsHosting.Compose(UnavailableAutomationServer.Instance, UnavailableFormHost.Instance, new FormsOptions { VB = VisualBasicFormsHost.Off });

        var failure = Assert.ThrowsExactly<AutomationException>(() => forms.Create(VisualBasicForm()));

        Assert.AreEqual(unchecked((int)0x80040154), failure.HResult);
    }
}
