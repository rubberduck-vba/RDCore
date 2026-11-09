using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Runtime.StdLib;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §6.1.2.10.1.1, §6.1.2.3.1.16, §6.1.2.3.1.18
// MS-VBA types Abs, Int and Fix of a numeric argument as the argument's type, where MS-VBAL declares them Variant: Fix(n) * 7000 of an Integer overflows.
// RD-VBA types the call as MS-VBA does, and warns where the difference shows.
public static class ArgumentTypedFunctions
{
    public static VBType? DeclaredTypeOfCall(Symbol procedure, VBType? argumentType)
        => argumentType is VBNumericType && IsTypedByItsArgument(procedure) ? argumentType : null;

    public static bool IsTypedByItsArgument(Symbol procedure)
        => procedure.GetProperty(SymbolProperties.ExternalTarget) is "IStdMathModule.Abs/1" or "IStdConversionModule.Int/1" or "IStdConversionModule.Fix/1"
            && StdLibSymbolProvider.IsLibraryMember(procedure);
}
