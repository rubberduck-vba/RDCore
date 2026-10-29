using RDCore.SDK.Model;
using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Directives;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Semantics;
using RDCore.SDK.Workspace;
using RDCore.SDK.Workspace.Forms;
using System.Collections.Immutable;

namespace RDCore.Symbols;

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
public sealed class SyntaxTreeSymbolProvider(
    Uri workspaceRoot, Uri moduleUri, ModuleType moduleType, ModuleParseResult parseResult, ISymbolResolver resolver,
    bool withImplicitDeclarations = true,
    ImplicitDeclarationScope implicitScope = ImplicitDeclarationScope.Procedure) : ISymbolProvider
{
    public IEnumerable<Symbol> ProvideSymbols() => ProvideDeclaredSymbols().Select(WithUserMemId).Select(WithDescription);

    // what a member's own module says it does (`Attribute Item.VB_Description = "..."`): what a completion list and a signature help show of it.
    private Symbol WithDescription(Symbol member)
        => member is VBTypeMemberSymbol typeMember && parseResult.SyntaxTree?.GetMemberDescription(typeMember.Name) is { } description
            ? typeMember.With(SymbolProperties.DocString, description)
            : member;

    // the id a member's own module gave it (`Attribute Item.VB_UserMemId = 0`): what marks the default member of a class and its enumeration member
    // (`_NewEnum`, -4). It is stamped here, on what the provider yields, so that both of its consumers have it - the resolver that binds the workspace, and the
    // host the symbols are defined to, which reads no source of its own to find it in.
    private Symbol WithUserMemId(Symbol member)
        => member is VBTypeMemberSymbol typeMember && parseResult.SyntaxTree?.GetMemberUserMemId(typeMember.Name) is { } userMemId
            ? typeMember.With(SymbolProperties.UserMemId, userMemId)
            : member;

    private IEnumerable<Symbol> ProvideDeclaredSymbols()
    {
        // one identity (same uri) can be declared once per #If branch — such sites are alternatives of one another, and fuse into the first, carrying every
        // site in Definitions and what each other declares in Alternatives. a site that is not an alternative of the others is not another branch of the same
        // declaration but a second declaration of the name, which is an error for whoever reads the symbols to find, and not one to hide in a fusion.
        // the accessors of one property have identities of their own (Symbol.UriSuffix), so they never fuse here. the identity is the uri alone, not the
        // symbol's type: a header written once per branch may declare a Sub in one and a Function in the other.
        var blocks = ConditionalCompilationBlocks.Of(parseResult.PrecompilerTrivia);
        foreach (var group in EnumerateDeclaredSymbols().GroupBy(symbol => symbol.Uri.ToString()))
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

            foreach (var alternatives in ClusterAlternatives(bound, blocks))
            {
                // the fused symbol is the first site's, and keeps what each other site declares: whoever knows which branch is live defines that one.
                yield return alternatives.Count == 1
                    ? alternatives[0]
                    : alternatives[0] with
                    {
                        Definitions = [.. alternatives.Select(symbol => new SymbolDefinition(symbol.Range, symbol.SelectionRange, DefinitionState.Unknown))],
                        Alternatives = [.. alternatives.Skip(1)],
                    };
            }
        }
    }

    // The sites of one declaration, in source order, in groups of sites that are each alternatives of every other site in the group: a site joins the first group
    // whose every site is in another branch than it, and begins a group of its own when none is - which is what a second declaration in the same branch is.
    // A name that every procedure of a module declares by mentioning it (the implicit variable of a module-scoped environment) is declared by none of
    // them in particular, and is the one variable however many sites there are.
    private static List<List<WorkspaceSymbol>> ClusterAlternatives(List<WorkspaceSymbol> sites, ConditionalCompilationBlocks blocks)
    {
        var groups = new List<List<WorkspaceSymbol>>();
        foreach (var site in sites)
        {
            var group = groups.FirstOrDefault(candidate => candidate.All(other
                => (IsImplicit(other) && IsImplicit(site)) || blocks.AreAlternatives(other.Range.Start, site.Range.Start)));
            if (group is null)
            {
                groups.Add([site]);
            }
            else
            {
                group.Add(site);
            }
        }

        return groups;
    }

    private static bool IsImplicit(WorkspaceSymbol symbol) => symbol.GetProperty(SymbolProperties.ImplicitlyDeclared);

    private IEnumerable<Symbol> EnumerateDeclaredSymbols()
    {
        if (parseResult.SyntaxTree is not { } module)
        {
            yield break;
        }

        // a standard module's members are module-scoped; a class module's are instance-scoped, and a document module is a class module.
        var memberScope = moduleType.IsClass() ? ScopeKind.Instance : ScopeKind.Module;
        // MS-VBAL 5.2.3.1.5: a declaration that names no type is implicitly a Variant, unless a
        // Def<Type> directive covers the first letter of its name (MS-VBAL 5.2.2).
        var builder = new SymbolBuilder(workspaceRoot, moduleUri, memberScope, resolver, VBVariantType.TypeInfo, [.. module.Children.OfType<TypeDefDirectiveNode>()]);

        // MS-VBAL 5.2.1.3: Option Explicit sets the module's variable declaration mode. Without it the
        // module is in implicit mode, where a reference to an undeclared name declares it (5.6.10).
        var directives = new ModuleDirectives(Explicit: module.HasOptionExplicit());

        // module-level names are order-independent, so collect them before walking the members — a
        // ReDim in one procedure may re-dimension a field declared further down the module.
        var moduleScopeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sites = new Dictionary<string, List<SourcePosition>>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in module.Children)
        {
            var name = child switch
            {
                VariableDeclarationNode field => field.Name,
                ConstantDeclarationNode { ConstKind: ConstKind.ModuleMember } constant => constant.Name,
                _ => null,
            };

            if (name is not null)
            {
                moduleScopeNames.Add(name);
                (sites.TryGetValue(name, out var positions) ? positions : sites[name] = []).Add(child.SourceLocation.Range.Start);
            }
        }

        // MS-VBAL §3.4: a variable or a constant the module declares only in the branches of #If blocks is declared only where one of them is live, which the
        // host knows and this does not. A name of one is matched here; where each is dead, the name matches nothing, and what a procedure declares by it is
        // declared then (SymbolProperties.UnlessLive).
        var blocks = ConditionalCompilationBlocks.Of(parseResult.PrecompilerTrivia);
        var conditional = sites.Where(site => site.Value.TrueForAll(blocks.IsConditional))
            .ToDictionary(site => site.Key, site => site.Value.ToImmutableArray(), StringComparer.OrdinalIgnoreCase);

        moduleScopeNames.UnionWith(parseResult.Designer?.DeclaredControls().Select(control => control.Name) ?? []);
        foreach (var control in parseResult.Designer?.DeclaredControls() ?? [])
        {
            conditional.Remove(control.Name);
        }

        foreach (var child in module.Children)
        {
            switch (child)
            {
                case ExternalMemberDeclarationNode external:
                    yield return builder.BuildExternal(external);
                    break;

                // a procedure whose header is written once in each branch of a #If block is declared by each of its headers, in its own branch: alternatives,
                // which fuse into one symbol below as any declaration written once per branch does.
                // The locals are the body's, which every header shares: an alternative of the same name declares the procedure, and not them again.
                case MemberDeclarationNode member:
                    foreach (var declared in member.WithEachHeader())
                    {
                        var symbols = FromMember(builder, declared, moduleScopeNames, conditional, directives, withImplicitDeclarations, implicitScope);
                        var isRepeat = !ReferenceEquals(declared, member) && string.Equals(declared.Name, member.Name, StringComparison.OrdinalIgnoreCase);
                        foreach (var symbol in isRepeat ? symbols.Take(1) : symbols)
                        {
                            yield return symbol;
                        }
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

        // a form's controls are members of its class that its designer declares, not its code. A name the code declares too is a compile error of the code's
        // declaration (DeclarationStaticSemanticsEvaluator.CheckDesigner); the code's declaration is the symbol of that name, so that each name has one. The
        // designer declares the form's own members alongside them: the VBA form object's and the Microsoft Forms UserForm's (SymbolBuilder.BuildFormMembers).
        if (parseResult.Designer is not { } designer)
        {
            yield break;
        }

        var taken = DeclaredNamesOf(module);
        foreach (var control in designer.DeclaredControls())
        {
            if (taken.Add(control.Name))
            {
                yield return builder.BuildDesignerControl(control, designer.Name);
            }
        }

        // a VB6 form is VB6's, whose runtime is out of scope: only a UserForm has the VBA form object
        if (designer.Kind == FormDesignerKind.MSForms)
        {
            foreach (var member in builder.BuildFormMembers(designer.Name, taken))
            {
                yield return member;
            }
        }
    }

    private static HashSet<string> DeclaredNamesOf(ModuleNode module)
        => new(module.Children.Select(child => child switch
        {
            VariableDeclarationNode variable => variable.Name,
            ConstantDeclarationNode constant => constant.Name,
            ExternalMemberDeclarationNode external => external.Name,
            MemberDeclarationNode member => member.Name,
            _ => string.Empty,
        }).Where(name => name.Length > 0), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<Symbol> FromMember(
        SymbolBuilder builder, MemberDeclarationNode member, IReadOnlySet<string> moduleScopeNames,
        IReadOnlyDictionary<string, ImmutableArray<SourcePosition>> conditional,
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
                var conditionalOuter = conditional.Where(site => !member.Children.OfType<ParameterDeclarationNode>()
                    .Any(parameter => string.Equals(parameter.Name, site.Key, StringComparison.OrdinalIgnoreCase)))
                    .ToDictionary(site => site.Key, site => site.Value, StringComparer.OrdinalIgnoreCase);
                foreach (var local in builder.BuildLocals(member, procedure.Uri, outerScopeNames, directives, withImplicitDeclarations, implicitScope, conditionalOuter))
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
