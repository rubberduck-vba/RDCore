using RDCore.SDK.Model;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.SDK.Runtime.Abstract.StdLib;

/// <summary>
/// <strong>MS-VBAL 6.1.3.3 Global Class</strong>: the subroutines that load and unload a <em>form</em>.
/// </summary>
/// <remarks>
/// <para>
/// VBA's <c>Global</c> is an application object: its members resolve unqualified, as a standard module's do (<c>Unload Me</c>), which is how it is declared here.
/// </para>
/// <para>
/// What a form is, and what loading and unloading it does, is the form host's: the Microsoft Forms compatibility layer, on Windows, which implements these. A
/// platform with no form host has no form to load, and says so when one is.
/// </para>
/// </remarks>
[StdLibModule("Global")]
public interface IStdGlobalClass
{
    #region 6.1.3.3.1 Public Subroutines
    /// <summary>
    /// <strong>MS-VBAL 6.1.3.3.1.1 Load</strong> Loads a <em>form</em> without showing it.
    /// </summary>
    /// <remarks>
    /// Any reference to a form loads it (a UserForm's <c>Initialize</c> runs then), so the argument has loaded it by the time this is called: what is left is to
    /// say that an object that is no form cannot be loaded (error 361).
    /// </remarks>
    /// <param name="object">The form.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult Load(VBObjectValue @object);

    /// <summary>
    /// <strong>MS-VBAL 6.1.3.3.1.2 Unload</strong> Unloads a <em>form</em>.
    /// </summary>
    /// <remarks>
    /// A UserForm is asked first (<c>QueryClose</c>, with <c>vbFormCode</c>), and stays loaded when it cancels; otherwise its <c>Terminate</c> runs, its window is
    /// destroyed, and a default instance is let go of, so that the next reference to it loads another (Excel-measured, <c>oracle\s41</c>).
    /// </remarks>
    /// <param name="object">The form.</param>
    /// <returns>A <see cref="RuntimeSemanticsEvaluationResult"/> object encapsulating the result of the successful operation, or the error metadata otherwise.</returns>
    public RuntimeSemanticsEvaluationResult Unload(VBObjectValue @object);
    #endregion

    #region Properties
    /// <summary>
    /// The forms that are loaded, in the order they were (<see cref="IStdUserFormsClass"/>).
    /// </summary>
    /// <remarks>
    /// Declared <c>As Object</c>, as MS-VBA declares it: its members are late-bound, and what they are is the collection's, not any interface's a program could
    /// name. It is the same object every time (Excel-measured, <c>oracle\s41</c> U01, U02).
    /// </remarks>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    public RuntimeSemanticsEvaluationResult<VBObjectValue> UserForms();
    #endregion
}

/// <summary>
/// The <c>UserForms</c> collection: the forms that are loaded, in the order they were, numbered from 0 (Excel-measured, <c>oracle\s41</c> U01-U19).
/// </summary>
/// <remarks>
/// Its class is one MS-VBA names nothing - <c>TypeName(UserForms)</c> is <c>Object</c> - and nothing can declare a variable of it: it is reached late-bound, through
/// <see cref="IStdGlobalClass.UserForms"/>. A member it does not have (<c>Remove</c>) is error 438, as any late-bound member that is not there is.
/// </remarks>
[StdLibClass("_UserForms", IsCreatable = false, TypeName = "Object")]
public interface IStdUserFormsClass
{
    /// <summary>The number of forms that are loaded.</summary>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBLongValue> Count();

    /// <summary>
    /// The loaded form at <paramref name="index"/>, from 0: its default member (<c>UserForms(0)</c>). An index that is no form's is error 9, and one that is no
    /// number - a form's name - error 13 (U04, U09, U17).
    /// </summary>
    /// <param name="index">The position of the form.</param>
    [StdLibMember(UserMemId = WellKnownDispIds.Value)]
    RuntimeSemanticsEvaluationResult<VBObjectValue> Item(VBLongValue index);

    /// <summary>
    /// Loads a new instance of the form named <paramref name="name"/> - not its default instance - and returns it; the collection holds it until it is unloaded.
    /// A name that is no form's is error 424 (U05-U07).
    /// </summary>
    /// <param name="name">The name of the form's module.</param>
    RuntimeSemanticsEvaluationResult<VBObjectValue> Add(VBStringValue name);

    /// <summary>The loaded forms, for <c>For Each</c> (U11, U18).</summary>
    [StdLibMember("_NewEnum", ReturnType = typeof(IStdEnumVariantClass), IsHidden = true, UserMemId = WellKnownDispIds.NewEnum)]
    RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum();
}
