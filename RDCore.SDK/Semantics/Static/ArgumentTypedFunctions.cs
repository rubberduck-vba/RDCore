using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// The functions of the standard library whose call is of the declared type of its argument: <c>Abs</c>, <c>Int</c> and <c>Fix</c> of a numeric value.
/// </summary>
/// <remarks>
/// <para>
/// <strong>MS-VBAL §6.1.2.10.1.1</strong> (<c>Abs</c>), <strong>§6.1.2.3.1.16</strong> (<c>Fix</c>) and <strong>§6.1.2.3.1.18</strong> (<c>Int</c>) declare each
/// <c>As Variant</c>, which returns "a value of the same value type" as a number it is given. MS-VBA types the call itself as its argument's declared type
/// when that is a numeric type: <c>Fix(n)</c> of an <c>Integer</c> is an <c>Integer</c>, and <c>Fix(n) * 7000</c> is a product of <c>Integer</c>s, which
/// overflows (error 6) where a <c>Variant</c> product would be a <c>Long</c> (recorded in Excel: oracle\s46 F10, as vb6.exe does). RD-VBA types the call as
/// MS-VBA does, and warns where the difference shows.
/// </para>
/// <para>
/// An argument of any other declared type - a <c>Variant</c>, a <c>String</c>, a <c>Boolean</c>, a <c>Date</c> - leaves the call a <c>Variant</c>, as declared.
/// </para>
/// </remarks>
public static class ArgumentTypedFunctions
{
    /// <summary>
    /// The declared type of a call of <paramref name="procedure"/> given an argument of <paramref name="argumentType"/>, when it is not the one
    /// <paramref name="procedure"/> declares: the argument's, for <c>Abs</c>, <c>Int</c> or <c>Fix</c> of a numeric value.
    /// </summary>
    /// <param name="procedure">The procedure called.</param>
    /// <param name="argumentType">The declared type of its one argument, if it is known.</param>
    /// <returns>The type; <see langword="null"/> when the call is of the type <paramref name="procedure"/> declares.</returns>
    public static VBType? DeclaredTypeOfCall(Symbol procedure, VBType? argumentType)
        => argumentType is VBNumericType && IsTypedByItsArgument(procedure) ? argumentType : null;

    /// <summary>
    /// Whether <paramref name="procedure"/> is the standard library's <c>Abs</c>, <c>Int</c> or <c>Fix</c>, whose call is of its argument's numeric type.
    /// </summary>
    /// <param name="procedure">The procedure.</param>
    public static bool IsTypedByItsArgument(Symbol procedure)
        => procedure.GetProperty(SymbolProperties.ExternalTarget) is "IStdMathModule.Abs/1" or "IStdConversionModule.Int/1" or "IStdConversionModule.Fix/1"
            && StdLibSymbolProvider.IsLibraryMember(procedure);
}
