using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Produces the member symbols a module declares, resolved from its parsed declaration AST. The tree
/// carries every conditional-compilation branch, so a name declared in more than one branch
/// (<c>#If VBA7 Then … #Else … #End If</c>) is collapsed into a single symbol whose
/// <see cref="WorkspaceSymbol.Definitions"/> lists each branch's declaration site; a later pass marks
/// the sites live or dead from the resolved <c>#Const</c> values.
/// </summary>
/// <remarks>
/// The containing module symbol is the project symbol provider's responsibility; library reference
/// symbols are the library symbol provider's. Procedure-local <c>Dim</c>/<c>Static</c>/<c>Const</c>
/// declarations — and the dynamic-array local an unresolved <c>ReDim</c> target implicitly declares —
/// are yielded as children of their procedure symbol.
/// </remarks>
/// <param name="withImplicitDeclarations">
/// Whether a reference to an undeclared name declares it (<strong>MS-VBAL §5.6.10</strong>). Only a
/// pass whose <paramref name="resolver"/> can actually answer "does this name resolve anywhere" may
/// do that: a workspace composition's first pass runs with the intrinsics alone, where nothing
/// resolves and every reference would declare a local, so it passes <c>false</c> and exists only to
/// discover what the workspace declares.
/// </param>
/// <param name="implicitScope">
/// Where the variable such a reference declares lives: a local of the procedure, as <strong>MS-VBAL §5.6.10</strong>
/// has it, or a variable of the module, as a BASIC does.
/// </param>
internal sealed class SyntaxTreeSymbolProvider(
    Uri workspaceRoot, Uri moduleUri, ModuleType moduleType, ModuleParseResult parseResult, ISymbolResolver resolver,
    bool withImplicitDeclarations = true,
    ImplicitDeclarationScope implicitScope = ImplicitDeclarationScope.Procedure) : ISymbolProvider
{
    public IEnumerable<Symbol> ProvideSymbols() => ProvideDeclaredSymbols().Select(WithUserMemId);

    // the id a member's own module gave it (`Attribute Item.VB_UserMemId = 0`): what marks the default member of a class and its enumeration member
    // (`_NewEnum`, -4). It is stamped here, on what the provider yields, so that both of its consumers have it - the resolver that binds the workspace, and the
    // host the symbols are defined to, which reads no source of its own to find it in.
    private Symbol WithUserMemId(Symbol member)
        => member is VBTypeMemberSymbol typeMember && parseResult.SyntaxTree?.GetMemberUserMemId(typeMember.Name) is { } userMemId
            ? typeMember.With(SymbolProperties.UserMemId, userMemId)
            : member;

    private IEnumerable<Symbol> ProvideDeclaredSymbols()
    {
        // one identity (same uri, same concrete symbol type) can be declared once per #If branch —
        // collapse each such group into the first site, carrying every site in Definitions. the accessors
        // of one property have identities of their own (Symbol.UriSuffix), so they never fuse here.
        foreach (var group in EnumerateDeclaredSymbols().GroupBy(symbol => (symbol.Uri.ToString(), symbol.GetType())))
        {
            var sites = group.ToList();
            if (sites.Count == 1)
            {
                yield return sites[0];
                continue;
            }

            var bound = sites.OfType<WorkspaceSymbol>().OrderBy(symbol => symbol.Range).ToList();
            if (bound.Count != sites.Count)
            {
                // unbound duplicates aren't expected from the ast — pass them through untouched.
                foreach (var site in sites)
                {
                    yield return site;
                }
                continue;
            }

            yield return bound[0] with
            {
                Definitions = [.. bound.Select(symbol => new SymbolDefinition(symbol.Range, symbol.SelectionRange, DefinitionState.Unknown))],
            };
        }
    }

    private IEnumerable<Symbol> EnumerateDeclaredSymbols()
    {
        if (parseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        // a standard module's members are module-scoped; a class module's are instance-scoped.
        var memberScope = moduleType == ModuleType.ClassModule ? ScopeKind.Instance : ScopeKind.Module;
        // MS-VBAL 5.2.3.1.5: a declaration that names no type is implicitly a Variant, unless a
        // Def<Type> directive covers the first letter of its name (MS-VBAL 5.2.2).
        var builder = new SymbolBuilder(workspaceRoot, moduleUri, memberScope, resolver, VBVariantType.TypeInfo, [.. module.Children.OfType<TypeDefDirectiveNode>()]);

        // MS-VBAL 5.2.1.3: Option Explicit sets the module's variable declaration mode. Without it the
        // module is in implicit mode, where a reference to an undeclared name declares it (5.6.10).
        var directives = new ModuleDirectives(Explicit: module.HasOptionExplicit());

        // module-level names are order-independent, so collect them before walking the members — a
        // ReDim in one procedure may re-dimension a field declared further down the module.
        var moduleScopeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in module.Children)
        {
            switch (child)
            {
                case VariableDeclarationNode field:
                    moduleScopeNames.Add(field.Name);
                    break;
                case ConstantDeclarationNode { ConstKind: ConstKind.ModuleMember } constant:
                    moduleScopeNames.Add(constant.Name);
                    break;
            }
        }

        foreach (var child in module.Children)
        {
            switch (child)
            {
                case ExternalMemberDeclarationNode external:
                    yield return builder.BuildExternal(external);
                    break;

                case MemberDeclarationNode member:
                    foreach (var symbol in FromMember(builder, member, moduleScopeNames, directives, withImplicitDeclarations, implicitScope))
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

    private static IEnumerable<Symbol> FromMember(
        SymbolBuilder builder, MemberDeclarationNode member, IReadOnlySet<string> moduleScopeNames,
        ModuleDirectives directives, bool withImplicitDeclarations, ImplicitDeclarationScope implicitScope)
    {
        switch (member.MemberKind)
        {
            case MemberKind.Procedure:
            case MemberKind.Function:
            case MemberKind.PropertyGet:
            case MemberKind.PropertyLet:
            case MemberKind.PropertySet:
                var procedure = member.MemberKind switch
                {
                    MemberKind.Procedure => builder.BuildProcedure(member),
                    MemberKind.Function => builder.BuildFunction(member),
                    MemberKind.PropertyGet => builder.BuildPropertyGet(member),
                    MemberKind.PropertyLet => builder.BuildPropertyLet(member),
                    MemberKind.PropertySet => builder.BuildPropertySet(member),
                    _ => throw new NotSupportedException($"{nameof(FromMember)} reached its procedure branch with a non-procedure {nameof(MemberKind)} '{member.MemberKind}'."),
                };
                yield return procedure;

                // names already visible from outside the body: this module's fields/consts, plus the
                // procedure's own parameters. a ReDim of one of these is a re-dimension, not a new local.
                var outerScopeNames = new HashSet<string>(moduleScopeNames, StringComparer.OrdinalIgnoreCase);
                foreach (var parameter in member.Children.OfType<ParameterDeclarationNode>())
                {
                    outerScopeNames.Add(parameter.Name);
                }

                // procedure-local Dim/Static/Const + ReDim-introduced symbols parent to the procedure symbol. An
                // implicit declaration does too, unless the environment has it declared at module level - then it
                // is a member of the module like any other variable of it, and arrives here with the rest.
                foreach (var local in builder.BuildLocals(member, procedure.Uri, outerScopeNames, directives, withImplicitDeclarations, implicitScope))
                {
                    yield return local;
                }
                break;
            case MemberKind.Event:
                yield return builder.BuildEvent(member);
                break;
            case MemberKind.UserDefinedType:
                var userDefinedType = (VBUserDefinedTypeMemberSymbol)builder.BuildUserDefinedType(member);
                var udtFields = member.Children.OfType<MemberDeclarationNode>()
                    .Where(field => field.MemberKind == MemberKind.UserDefinedTypeField)
                    .Select(field => builder.BuildUserDefinedTypeField(field, userDefinedType.Uri))
                    .ToArray();
                // the fields ride on the type symbol (so a resolver returns a whole VBUserDefinedType)
                // and are also yielded on their own, parented to it.
                yield return userDefinedType with { Members = [.. udtFields.Cast<VBTypeMemberSymbol>()] };
                foreach (var udtField in udtFields)
                {
                    yield return udtField;
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
