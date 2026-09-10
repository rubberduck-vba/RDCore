using RDCore.SDK.Model.AST;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.LanguageServer.Symbols;

/// <summary>
/// Produces the member symbols a module declares, resolved from its parsed declaration AST. The tree
/// carries every conditional-compilation branch, so a name declared in more than one branch
/// (<c>#If VBA7 Then … #Else … #End If</c>) is collapsed into a single symbol whose
/// <see cref="BoundSymbol.Definitions"/> lists each branch's declaration site; a later pass marks
/// the sites live or dead from the resolved <c>#Const</c> values.
/// </summary>
/// <remarks>
/// The containing module symbol is the project symbol provider's responsibility; library reference
/// symbols are the library symbol provider's. Procedure-local <c>Dim</c>/<c>Static</c>/<c>Const</c>
/// declarations are yielded as children of their procedure symbol; symbols a <c>ReDim</c> introduces
/// under <c>Option Explicit</c> are not discovered yet (the parser doesn't emit that node).
/// </remarks>
internal sealed class SyntaxTreeSymbolProvider(
    Uri workspaceRoot, Uri moduleUri, ModuleParseResult parseResult, ISymbolResolver resolver) : ISymbolProvider
{
    public IEnumerable<Symbol> ProvideSymbols()
    {
        // one identity (same uri, same concrete symbol type) can be declared once per #If branch —
        // uri alone would also fuse Property Get/Let/Set, which must stay distinct. collapse each
        // such group into the first site, carrying every site in Definitions.
        // FIXME Property Get/Let/Set of one name share a Symbol.Uri (Symbol.CreateUri keys on name
        // only); the concrete-type part of this key is what keeps them apart here, and _idMap in the
        // session is still last-wins across them. give accessors a distinct semantic id (e.g. a
        // "/get" | "/let" | "/set" uri suffix) — deferred, it's an identity change across the symbol
        // ctors, SymbolDescriptorReader.ChildUri and every by-name uri lookup.
        foreach (var group in EnumerateDeclaredSymbols().GroupBy(symbol => (symbol.Uri.ToString(), symbol.GetType())))
        {
            var sites = group.ToList();
            if (sites.Count == 1)
            {
                yield return sites[0];
                continue;
            }

            var bound = sites.OfType<BoundSymbol>().OrderBy(symbol => symbol.Range).ToList();
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
                // procedure-local Dim/Static/Const symbols parent to the procedure symbol.
                foreach (var local in builder.BuildLocals(member, procedure.Uri))
                {
                    yield return local;
                }
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
