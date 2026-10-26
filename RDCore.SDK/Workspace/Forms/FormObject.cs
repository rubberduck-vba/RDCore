using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Libraries;

namespace RDCore.SDK.Workspace.Forms;

/// <summary>
/// What VBA makes of a UserForm besides the Microsoft Forms <c>UserForm</c> it hosts: the members and the events of the form object, which a form's class
/// has as well as its controls (<see cref="FormDescriptor.DeclaredControls"/>) and the members of its own code.
/// </summary>
/// <remarks>
/// <para>
/// <c>UserForm1.Show</c>, <c>Me.Hide</c>, <c>UserForm1.Left</c> and the <c>Initialize</c>, <c>QueryClose</c>, <c>Terminate</c> events are VBA's, not Microsoft
/// Forms': FM20.DLL's <c>UserForm</c> declares none of them, and no type library on disk does (VBE7.DLL's describes the VBA standard library). The form's
/// class is not extensible (<c>UserForm1.ShowModal</c> is "Method or data member not found"), so each member here is one MS-VBA compiles, measured in Excel
/// (<c>oracle\s32b</c>, <c>s32c</c>): what it is (a method, or a property MS-VBA reads and, for some, assigns), and what its arguments are. The types of the
/// properties are what <c>TypeName</c> answers of them; the parameters of <c>Show</c> and <c>Move</c>, whose types MS-VBA coerces to, are <c>Variant</c>.
/// </para>
/// <para>
/// The events are those the VBA reference documents for a UserForm, which handlers named <c>UserForm_EventName</c> handle, whatever the form's name.
/// </para>
/// <para>
/// The description is a library's (<see cref="LibraryDescription"/>), read as one is (<see cref="LibrarySymbolReader"/>), so that its members are symbols like
/// any library class's: the designer declares them on the form's class (<see cref="IsDeclaredBy"/>), and a call of one is dispatched outside the workspace's
/// code, by its <see cref="SymbolProperties.ExternalTarget"/>, to whatever hosts the form.
/// </para>
/// </remarks>
public static class FormObject
{
    /// <summary>
    /// The name of the pseudo-library the form object's members are read from: the first part of their <see cref="SymbolProperties.ExternalTarget"/>.
    /// </summary>
    public const string LibraryName = "UserForms";

    /// <summary>
    /// The name of the form object's class in it, and the prefix of the handlers of its events (<c>UserForm_Initialize</c>).
    /// </summary>
    public const string ClassName = "UserForm";

    private static MemberDescription Get(string name, string type) => new() { Name = name, Kind = MemberKind.PropertyGet, Type = type };

    private static MemberDescription Let(string name, string type)
        => new() { Name = name, Kind = MemberKind.PropertyLet, Parameters = [new ParameterDescription { Name = name, Type = type, IsByVal = true }] };

    private static MemberDescription Method(string name, params ParameterDescription[] parameters)
        => new() { Name = name, Kind = MemberKind.Method, Parameters = [.. parameters] };

    private static ParameterDescription Optional(string name) => new() { Name = name, Type = "Variant", IsByVal = true, IsOptional = true };

    /// <summary>
    /// The form object, as a library of one class.
    /// </summary>
    public static LibraryDescription Description { get; } = new()
    {
        Name = LibraryName,
        Description = "The VBA form object of a UserForm",
        Classes =
        [
            new ClassDescription
            {
                Name = ClassName,
                Members =
                [
                    // s32b M01-M05, M90, M91, s32c A20-A25: methods; Show takes one argument at most, Move one at least
                    Method("Show", Optional("Modal")),
                    Method("Hide"),
                    Method("Move", new ParameterDescription { Name = "Left", Type = "Variant", IsByVal = true }, Optional("Top"), Optional("Width"), Optional("Height")),
                    Method("PrintForm"),
                    Method("WhatsThisMode"),

                    // s32b M06-M18, s32c A01-A12, A26: properties; assignable ones have a Let, and the others' Let is restricted (an assignment does not compile)
                    Get("Left", "Single"), Let("Left", "Single"),
                    Get("Top", "Single"), Let("Top", "Single"),
                    Get("Width", "Single"), Let("Width", "Single"),
                    Get("Height", "Single"), Let("Height", "Single"),
                    Get("Tag", "String"), Let("Tag", "String"),
                    Get("StartUpPosition", "Integer"), Let("StartUpPosition", "Integer"),
                    Get("HelpContextID", "Long"), Let("HelpContextID", "Long"),
                    Get("RightToLeft", "Boolean"), Let("RightToLeft", "Boolean"),
                    Get("Visible", "Boolean"),
                    Get("Name", "String"),
                    Get("WhatsThisButton", "Boolean"),
                    Get("WhatsThisHelp", "Boolean"),
                ],
                Events =
                [
                    new EventDescription { Name = "Initialize" },
                    new EventDescription { Name = "Activate" },
                    new EventDescription { Name = "Deactivate" },
                    new EventDescription
                    {
                        Name = "QueryClose",
                        Parameters = [new ParameterDescription { Name = "Cancel", Type = "Integer" }, new ParameterDescription { Name = "CloseMode", Type = "Integer" }],
                    },
                    new EventDescription { Name = "Resize" },
                    new EventDescription { Name = "Terminate" },
                ],
            },
        ],
    };

    /// <summary>
    /// The form object's class, read as a library's (<see cref="LibrarySymbolReader"/>), with its members and its events.
    /// </summary>
    /// <param name="workspaceRoot">The root of the workspace the symbols are of.</param>
    public static VBClassModuleSymbol ClassIn(Uri workspaceRoot)
        => new LibrarySymbolReader(workspaceRoot).Read([Description]).OfType<VBClassModuleSymbol>().Single(symbol => symbol.Name == ClassName);

    /// <summary>
    /// The <see cref="SymbolProperties.ExternalTarget"/> of a member a form's designer declares on its class: <c>UserForms.FormName.Member/kind</c>, which a host
    /// of the form dispatches; nothing does before one is part of the platform, and a call of it is then a run-time error, not a missing procedure.
    /// </summary>
    /// <param name="form">The name of the form.</param>
    /// <param name="member">The name of the member: a control, or a member of the form object or of the Microsoft Forms <c>UserForm</c>.</param>
    /// <param name="kind">What it is: <c>method</c>, <c>get</c>, <c>let</c> or <c>set</c>.</param>
    public static string TargetOf(string form, string member, string kind) => $"{LibraryName}.{form}.{member}/{kind}";

    /// <summary>
    /// Whether a member of a class is one its form's designer declares, rather than its code: a control, or a member of the form object or of the Microsoft
    /// Forms <c>UserForm</c> - which is what makes the class a form's.
    /// </summary>
    /// <param name="member">A member of a class module.</param>
    public static bool IsDeclaredBy(Symbol member)
        => member.GetProperty(SymbolProperties.ExternalTarget) is { } target && target.StartsWith(LibraryName + ".", StringComparison.Ordinal);

    /// <summary>
    /// Whether a class module is a form's: one whose designer declares members of it (<see cref="IsDeclaredBy"/>). Its lifecycle is a UserForm's - its
    /// <c>UserForm_Initialize</c> and <c>UserForm_Terminate</c> run, not a class module's <c>Class_Initialize</c> and <c>Class_Terminate</c> (Excel-measured,
    /// <c>oracle\s41</c> L50) - and it is what <c>Load</c> and <c>Unload</c> load and unload.
    /// </summary>
    /// <param name="classModule">The class module.</param>
    public static bool IsFormClass(VBClassModuleSymbol classModule) => classModule.Members.Any(IsDeclaredBy);
}
