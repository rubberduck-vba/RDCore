using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Produces the member symbols a module declares, resolved from its parsed declaration AST. Both live
/// and dead conditional-compilation branches are present in the tree, so both are yielded — the
/// language server keeps or drops each one once it knows the live <c>#Const</c> values.
/// </summary>
/// <remarks>
/// The containing module symbol is the project symbol provider's responsibility; library reference
/// symbols are the library symbol provider's. Statement-body locals and user-defined-type fields are
/// not yielded yet (the parser doesn't emit those nodes).
/// </remarks>
internal sealed class SyntaxTreeSymbolProvider(
    Uri workspaceRoot, Uri moduleUri, ModuleParseResult parseResult, ISymbolResolver resolver) : ISymbolProvider
{
    public IEnumerable<Symbol> ProvideSymbols()
    {
        if (parseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        // a standard module's members are module-scoped; a class module's are instance-scoped.
        var memberScope = module.ModuleType == ModuleType.ClassModule ? ScopeKind.Instance : ScopeKind.Module;
        var builder = new SymbolBuilder(workspaceRoot, moduleUri, memberScope, resolver);
        foreach (var child in module.Children)
        {
            switch (child)
            {
                case ExternalMemberDeclarationNode external:
                    yield return builder.BuildExternal(external);
                    break;

                case MemberDeclarationNode member:
                    foreach (var symbol in FromMember(builder, member))
                    {
                        yield return symbol;
                    }
                    break;

                case VariableDeclarationNode field:
                    yield return builder.BuildModuleField(field);
                    break;

                case ConstantDeclarationNode constant when constant.ConstKind == ConstKind.ModuleMember:
                    yield return builder.BuildConstant(constant);
                    break;
            }
        }
    }

    private static IEnumerable<Symbol> FromMember(SymbolBuilder builder, MemberDeclarationNode member)
    {
        switch (member.MemberKind)
        {
            case MemberKind.Procedure:
                yield return builder.BuildProcedure(member);
                break;
            case MemberKind.Function:
                yield return builder.BuildFunction(member);
                break;
            case MemberKind.PropertyGet:
                yield return builder.BuildPropertyGet(member);
                break;
            case MemberKind.PropertyLet:
                yield return builder.BuildPropertyLet(member);
                break;
            case MemberKind.PropertySet:
                yield return builder.BuildPropertySet(member);
                break;
            case MemberKind.Event:
                yield return builder.BuildEvent(member);
                break;
            case MemberKind.UserDefinedType:
                var userDefinedType = builder.BuildUserDefinedType(member);
                yield return userDefinedType;
                foreach (var udtField in member.Children.OfType<MemberDeclarationNode>()
                    .Where(field => field.MemberKind == MemberKind.UserDefinedTypeField))
                {
                    yield return builder.BuildUserDefinedTypeField(udtField, userDefinedType.Uri);
                }
                break;
            case MemberKind.Enum:
                var enumSymbol = builder.BuildEnum(member);
                yield return enumSymbol;
                foreach (var enumConst in builder.BuildEnumMembers(member, enumSymbol.Uri))
                {
                    yield return enumConst;
                }
                break;
        }
    }
}
