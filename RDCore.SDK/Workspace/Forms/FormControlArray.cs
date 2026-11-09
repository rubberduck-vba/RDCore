using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Runtime.Libraries;

namespace RDCore.SDK.Workspace.Forms;

/// <summary>
/// What a Visual Basic 6.0 form's control array is: the controls of one class that its designer persists under one name, each with its own <c>Index</c>, which
/// the form's class has one member for (<see cref="FormDescriptor.DeclaredControls"/>). <c>Command1(0)</c> is the control of index 0, and <c>Command1.Count</c>,
/// <c>Command1.LBound</c> and <c>Command1.UBound</c> are how many controls the array has and the least and the greatest of their indexes.
/// </summary>
/// <remarks>
/// <para>
/// No type library describes a control array: VB6.OLB declares the classes of the controls, not the object that holds them. The array's class is described
/// here as a library's (<see cref="LibraryDescription"/>), read as one is (<see cref="LibrarySymbolReader"/>), with one class for each class of control. Its
/// default member takes the index and returns the control, as the control's class; <c>Count</c>, <c>LBound</c> and <c>UBound</c> are <c>Integer</c>, as the
/// Visual Basic 6.0 documentation gives them. It has no other member, and no event: the events of a control of the array are its class's, and a handler of one
/// takes the control's index first (<c>MemberRoles</c>).
/// </para>
/// <para>
/// The name of the array's class is the platform's, not Visual Basic's: no code can name it, and what describes the array to a user names the class of its
/// controls (<see cref="ElementOf"/>).
/// </para>
/// </remarks>
public static class FormControlArray
{
    /// <summary>
    /// The name of the pseudo-library the classes of control arrays are read from: the <see cref="SymbolProperties.Library"/> of their symbols.
    /// </summary>
    public const string LibraryName = "ControlArrays";

    // the default member, which takes the index; hidden, and a name no code writes but [_Default].
    private const string DefaultMemberName = "_Default";

    private static MemberDescription Get(string name, string type) => new() { Name = name, Kind = MemberKind.PropertyGet, Type = type };

    /// <summary>
    /// The type of a control array whose controls are of <paramref name="element"/>.
    /// </summary>
    /// <param name="element">The type of the controls of the array: their class, or the name of one that did not resolve.</param>
    /// <param name="workspaceRoot">The root of the workspace the symbols are of.</param>
    public static VBClassType TypeOf(VBType element, Uri workspaceRoot)
    {
        var description = new LibraryDescription
        {
            Name = LibraryName,
            Description = "The control arrays of a Visual Basic 6.0 form",
            Classes =
            [
                new ClassDescription
                {
                    Name = ClassNameOf(element),
                    Members =
                    [
                        new MemberDescription
                        {
                            Name = DefaultMemberName,
                            Kind = MemberKind.PropertyGet,
                            DispId = 0,
                            IsHidden = true,
                            Parameters = [new ParameterDescription { Name = "Index", Type = "Integer", IsByVal = true }],
                        },
                        Get("Count", "Integer"),
                        Get("LBound", "Integer"),
                        Get("UBound", "Integer"),
                    ],
                },
            ],
        };

        var read = new LibrarySymbolReader(workspaceRoot).Read([description]).OfType<VBClassModuleSymbol>().Single();

        // the description has no name for the control's class, which is another library's: the default member returns it as it is.
        var symbol = read with
        {
            Members = [.. read.Members.Select(member => member is VBPropertyGetMemberSymbol { Name: DefaultMemberName } item ? item with { ResolvedType = element } : member)],
        };
        symbol = symbol with { DefaultInterfaceMembers = VBClassType.FromClassModule(symbol).Members };
        return VBClassType.Of(symbol);
    }

    /// <summary>
    /// The type of the controls of a control array, when <paramref name="type"/> is one's (<see cref="TypeOf"/>); <see langword="null"/> for any other type.
    /// </summary>
    /// <param name="type">A type.</param>
    public static VBType? ElementOf(VBType? type)
        => type is VBClassType { Symbol: var symbol } && symbol.GetProperty(SymbolProperties.Library) == LibraryName
            ? symbol.Members.OfType<VBPropertyGetMemberSymbol>().FirstOrDefault(member => member.Name == DefaultMemberName)?.ResolvedType
            : null;

    // one class for each class of control, told apart by its library as a type library's classes are.
    private static string ClassNameOf(VBType element)
        => (element is VBClassType { Symbol: var symbol } && symbol.GetProperty(SymbolProperties.Library) is { Length: > 0 } library
            ? $"{library}.{element.Name}"
            : element.Name).Replace('.', '_');
}
