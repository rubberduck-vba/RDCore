using RDCore.SDK.Model;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Shared;

namespace RDCore.Runtime.LanguageCore.StdLib.Abstract;

/// <summary>
/// The <c>Forms</c> collection of a Visual Basic 6.0 program: the forms that are loaded, in the order they were, numbered from 0 (measured in VB6).
/// </summary>
/// <remarks>
/// Its class has no name in VB6 (<c>TypeName(Forms)</c> is <c>Object</c>), and it is reached late-bound through the VB library's global <c>Forms</c>. It has
/// no keys, and no <c>Add</c> or <c>Remove</c>: a member it does not have is error 438, as for any late-bound member that is missing.
/// </remarks>
[StdLibClass("_Forms", IsCreatable = false, TypeName = "Object")]
public interface IStdFormsClass
{
    /// <summary>
    /// The number of forms that are loaded.
    /// </summary>
    [StdLibMember(Kind = StdLibMemberKind.PropertyGet)]
    RuntimeSemanticsEvaluationResult<VBIntegerValue> Count();

    /// <summary>
    /// The loaded form at <paramref name="index"/>, from 0: the default member (<c>Forms(0)</c>). The index is converted as <c>CInt</c> converts it. An index
    /// outside the loaded forms is error 9; a <c>String</c> that is not a number (a form's name included) or <c>Null</c> is error 13; an index too large for an
    /// <c>Integer</c> is error 6.
    /// </summary>
    /// <param name="index">The position of the form.</param>
    [StdLibMember(UserMemId = WellKnownDispIds.Value)]
    RuntimeSemanticsEvaluationResult<VBObjectValue> Item(VBVariantValue index);

    /// <summary>
    /// The forms that are loaded when a <c>For Each</c> begins: one unloaded during it is still handed out, and one loaded during it is not.
    /// </summary>
    [StdLibMember("_NewEnum", ReturnType = typeof(IStdEnumVariantClass), IsHidden = true, UserMemId = WellKnownDispIds.NewEnum)]
    RuntimeSemanticsEvaluationResult<VBObjectValue> NewEnum();
}
