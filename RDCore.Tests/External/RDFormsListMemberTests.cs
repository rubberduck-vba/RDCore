using RDCore.External.Automation;
using RDCore.External.Forms;
using RDCore.External.Forms.RDForms;
using RDCore.SDK.Workspace.Forms;
using System.Globalization;

namespace RDCore.Tests.External;

/// <summary>
/// A Visual Basic 6.0 ListBox's and ComboBox's list members, reached by name through the RDForms host's automation server as a program reaches them: the form's
/// window answers them, a designed list and one that <c>Controls.Add</c> adds alike.
/// </summary>
[TestClass]
[TestCategory("External host")]
public sealed class RDFormsListMemberTests
{
    private const int VisualBasicError = unchecked((int)0x800A0000);

    private const string Designer =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Players\"\r\n" +
        "   ClientHeight    =   3195\r\n" +
        "   ClientWidth     =   4680\r\n" +
        "   Begin VB.ListBox lstPlayers \r\n" +
        "      Height          =   1035\r\n" +
        "      Left            =   120\r\n" +
        "      TabIndex        =   0\r\n" +
        "      Top             =   120\r\n" +
        "      Width           =   2055\r\n" +
        "   End\r\n" +
        "   Begin VB.ComboBox cboPlayer \r\n" +
        "      Height          =   315\r\n" +
        "      Left            =   2400\r\n" +
        "      Style           =   2  'Dropdown List\r\n" +
        "      TabIndex        =   1\r\n" +
        "      Top             =   120\r\n" +
        "      Width           =   2055\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"Form1\"\r\n";

    private static RDFormsFormHost Host() => new(new RDFormsAutomationServer(RDFormsPlatform.Headless));

    private static FormDefinition VisualBasicForm()
        => new("Form1", ReadOnlyMemory<byte>.Empty) { Kind = FormDesignerKind.VisualBasic6, Caption = "Form1", DesignerText = Designer };

    private static object? Get(RDFormsFormHost host, object target, string member, params object?[] arguments)
        => host.Server.Invoke(target, member, AutomationInvocation.Get, arguments, new bool[arguments.Length], CultureInfo.InvariantCulture);

    private static void Call(RDFormsFormHost host, object target, string member, params object?[] arguments)
        => host.Server.Invoke(target, member, AutomationInvocation.Method, arguments, new bool[arguments.Length], CultureInfo.InvariantCulture);

    // the arguments, then the value, last.
    private static void Let(RDFormsFormHost host, object target, string member, params object?[] argumentsThenValue)
        => host.Server.Invoke(target, member, AutomationInvocation.Let, argumentsThenValue, new bool[argumentsThenValue.Length], CultureInfo.InvariantCulture);

    private static int ErrorOf(Action call)
        => Assert.ThrowsExactly<AutomationException>(call).HResult;

    private static void OnForm(Action<RDFormsFormHost, object> test)
    {
        var host = Host();
        var form = host.Create(VisualBasicForm());
        try
        {
            test(host, form);
        }
        finally
        {
            host.Destroy(form);
        }
    }

    [TestMethod]
    [DataRow("lstPlayers")]
    [DataRow("cboPlayer")]
    public void AddItem_AddsItems_ThatListCountCountsAndListReads(string name) => OnForm((host, form) =>
    {
        var list = Get(host, form, name)!;

        Call(host, list, "AddItem", "Human");
        Call(host, list, "AddItem", "Merciless AI");
        Call(host, list, "AddItem", "Random AI", 0);

        Assert.AreEqual((short)3, Get(host, list, "ListCount"));
        Assert.AreEqual("Random AI", Get(host, list, "List", 0));
        Assert.AreEqual("Merciless AI", Get(host, list, "List", 2));
        Assert.AreEqual((short)0, Get(host, list, "NewIndex"), "the index the last AddItem gave");
        Assert.AreEqual("", Get(host, list, "List", 3), "an index outside the items reads empty");
    });

    [TestMethod]
    [DataRow("lstPlayers")]
    [DataRow("cboPlayer")]
    public void ListIndex_SelectsAnItem_WhoseTextTextReads(string name) => OnForm((host, form) =>
    {
        var list = Get(host, form, name)!;
        Call(host, list, "AddItem", "Human");
        Call(host, list, "AddItem", "Merciless AI");

        Assert.AreEqual((short)-1, Get(host, list, "ListIndex"));
        Let(host, list, "ListIndex", 1);

        Assert.AreEqual((short)1, Get(host, list, "ListIndex"));
        Assert.AreEqual("Merciless AI", Get(host, list, "Text"));
        Assert.AreEqual("Merciless AI", Get(host, list, "[DISPID=0]"), "Text is the list's default member");
        Assert.AreEqual(VisualBasicError | 380, ErrorOf(() => Let(host, list, "ListIndex", 2)));
    });

    [TestMethod]
    [DataRow("lstPlayers")]
    [DataRow("cboPlayer")]
    public void ItemDataAndList_AreAssignedByIndex_TheValueLast(string name) => OnForm((host, form) =>
    {
        var list = Get(host, form, name)!;
        Call(host, list, "AddItem", "one");

        Let(host, list, "ItemData", 0, 100000);
        Let(host, list, "List", 0, 1);

        Assert.AreEqual(100000, Get(host, list, "ItemData", 0));
        Assert.AreEqual("1", Get(host, list, "List", 0), "a number assigned to List is its text");
        Assert.AreEqual(VisualBasicError | 381, ErrorOf(() => Get(host, list, "ItemData", 1)));
    });

    [TestMethod]
    [DataRow("lstPlayers")]
    [DataRow("cboPlayer")]
    public void RemoveItemAndClear_RemoveItems_And5ForAnIndexOutsideThem(string name) => OnForm((host, form) =>
    {
        var list = Get(host, form, name)!;
        Call(host, list, "AddItem", "a");
        Call(host, list, "AddItem", "b");

        Call(host, list, "RemoveItem", 0);
        Assert.AreEqual("b", Get(host, list, "List", 0));
        Assert.AreEqual((short)-1, Get(host, list, "NewIndex"));
        Assert.AreEqual(VisualBasicError | 5, ErrorOf(() => Call(host, list, "RemoveItem", 1)));

        Call(host, list, "Clear");
        Assert.AreEqual((short)0, Get(host, list, "ListCount"));
    });

    [TestMethod]
    [DataRow("lstPlayers", "ListCount")]
    [DataRow("lstPlayers", "NewIndex")]
    [DataRow("lstPlayers", "SelCount")]
    [DataRow("cboPlayer", "ListCount")]
    [DataRow("cboPlayer", "NewIndex")]
    public void AReadOnlyListProperty_IsError383(string name, string member) => OnForm((host, form) =>
    {
        var list = Get(host, form, name)!;

        var failure = Assert.ThrowsExactly<AutomationException>(() => Let(host, list, member, 0));

        Assert.AreEqual(VisualBasicError | 383, failure.HResult);
        StringAssert.Contains(failure.Message, member);
    });

    [TestMethod]
    public void AListBoxsSelected_AndSelCount() => OnForm((host, form) =>
    {
        var list = Get(host, form, "lstPlayers")!;
        Call(host, list, "AddItem", "one");
        Call(host, list, "AddItem", "two");

        Let(host, list, "Selected", 1, true);

        Assert.AreEqual(true, Get(host, list, "Selected", 1));
        Assert.AreEqual((short)1, Get(host, list, "SelCount"));
    });

    // Column and List(row, column) are an MSForms ListBox's: VB6's ListBox has no Column (438), and its List takes one index (450).
    [TestMethod]
    public void AVisualBasicListBox_HasNoColumn_AndItsListTakesOneIndex() => OnForm((host, form) =>
    {
        var list = Get(host, form, "lstPlayers")!;
        Call(host, list, "AddItem", "one");

        Assert.AreEqual(unchecked((int)0x80020006), ErrorOf(() => Get(host, list, "Column", 0, 0)), "DISP_E_UNKNOWNNAME: error 438");
        Assert.AreEqual(VisualBasicError | 450, ErrorOf(() => Get(host, list, "List", 0, 0)));
    });

    [TestMethod]
    public void ADesignedComboBox_TakesAddItemListIndexAndVisible() => OnForm((host, form) =>
    {
        var combo = Get(host, form, "cboPlayer")!;

        Call(host, combo, "AddItem", "Human");
        Call(host, combo, "AddItem", "Merciless AI");
        Let(host, combo, "ListIndex", 1);
        Let(host, combo, "Visible", false);

        Assert.AreEqual("Merciless AI", Get(host, combo, "Text"));
        Assert.AreEqual(false, Get(host, combo, "Visible"));
    });

    [TestMethod]
    public void AComboBoxThatControlsAddAdds_TakesTheListMembers() => OnForm((host, form) =>
    {
        var controls = Get(host, form, "Controls")!;
        var combo = host.Server.Invoke(controls, "Add", AutomationInvocation.Method, ["VB.ComboBox", "cboAdded"], [false, false], CultureInfo.InvariantCulture)!;

        Call(host, combo, "AddItem", "Human");
        Call(host, combo, "AddItem", "Merciless AI");
        Let(host, combo, "ListIndex", 1);
        Let(host, combo, "Visible", true);

        Assert.AreEqual((short)2, Get(host, combo, "ListCount"));
        Assert.AreEqual(true, Get(host, combo, "Visible"));
        Assert.AreEqual("Merciless AI", Get(host, combo, "Text"));
    });
}
