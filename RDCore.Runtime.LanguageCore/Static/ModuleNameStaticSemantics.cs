using RDCore.Runtime.LanguageCore.Errors;
using RDCore.SDK;
using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Errors;
using RDCore.SDK.Model.Source;
using System.Collections.Immutable;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §4.2: a module's VB_Name is a quoted-identifier, and an IDENTIFIER is not a reserved-identifier (§3.3.5.2). A module named by its file, which
// says no VB_Name, is named by the same rule. MS-VBA refuses such a name when a module is renamed, and when a standard module is imported ("Not a legal
// object name"). A reserved-type-identifier that is not a reserved-name (Long) is an IDENTIFIER, which MS-VBA refuses too: RD-VBA compiles it, and warns
// of it (RDC11034). A name that is not a Latin-identifier at all is not checked here: a client's own module of the session is named so on purpose
// (_Immediate), and the host is not told which module is the client's.
public static class ModuleNameStaticSemantics
{
    public static ImmutableArray<VBCompileErrorInfo> Check(ModuleNode module, string name)
    {
        if (!VBIdentifier.IsReservedIdentifier(name))
        {
            return [];
        }

        // the name is reported where the module says it, and at the start of the module when its file says it.
        var location = module.GetDeclaredNameDirective()?.Location ?? new SourceLocation(module.SourceLocation.Uri, new SourceRange(0, 0, 0, 0));
        return [VBCompileErrorInfo.For(VBCompileErrorId.NotALegalObjectName, location,
            string.Format(Exceptions.VBCompileError_NotALegalObjectName_Verbose, name))];
    }
}
