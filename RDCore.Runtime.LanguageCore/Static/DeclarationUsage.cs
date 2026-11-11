using RDCore.SDK.Model;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Workspace.Forms;
using System.Collections.Immutable;
using RDCore.SDK.Semantics;

namespace RDCore.Runtime.LanguageCore.Static;

// a count is stated only when it is the whole truth: nothing outside the analyzed code can refer to the declaration, and that code was fully analyzed.
public static class DeclarationUsage
{
    public static ImmutableArray<DeclarationFact> Of(
        IEnumerable<Symbol> declared, IEnumerable<ProcedureSemanticModel> procedures, IReadOnlyDictionary<SemanticId, DeclarationRole>? roles = null,
        Func<Symbol, HiddenDeclaration?>? hides = null)
    {
        var models = procedures.ToList();
        var references = new Dictionary<SemanticId, DeclarationReferences>();
        foreach (var fact in models.SelectMany(procedure => procedure.Expressions.Values).Where(fact => fact.Binding is not null))
        {
            var current = references.GetValueOrDefault(fact.Binding!.Value);
            references[fact.Binding.Value] = fact.Flags switch
            {
                var flags when flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) => current with { Writes = current.Writes + 1 },
                var flags when flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument) => current with { PassedAsArguments = current.PassedAsArguments + 1 },
                _ => current with { Reads = current.Reads + 1 },
            };
        }

        var moduleIsFullyAnalyzed = models.All(procedure => procedure.IsFullyAnalyzed);

        // the accessors of a property are one declaration, though each has the identity of its own that an expression can be bound to: it is the first one
        // that stands for it.
        var candidates = declared
            .Select(symbol => (Symbol: symbol, Kind: KindOf(symbol)))
            .Where(candidate => candidate.Kind is not null)
            .DistinctBy(candidate => candidate.Symbol.SemanticId)
            .ToList();
        var seenProperties = new HashSet<(string Module, string Name)>();

        var facts = ImmutableArray.CreateBuilder<DeclarationFact>();
        foreach (var (symbol, kind) in candidates)
        {
            if (kind is DeclarationKind.Property && !seenProperties.Add((symbol.ParentUri.AbsoluteUri, symbol.Name.ToUpperInvariant())))
            {
                continue;
            }

            var role = roles is not null && roles.TryGetValue(symbol.SemanticId, out var found) ? found : DeclarationRole.None;
            DeclarationReferences? counted = null;
            if ((IsAccessibleOnlyFromItsOwnCode(symbol, kind!.Value) || IsCalledOnlyByItsOwnCode(symbol, role))
                && CodeThatCanReferToIt(symbol, models, moduleIsFullyAnalyzed))
            {
                counted = references.GetValueOrDefault(symbol.SemanticId);
            }

            facts.Add(new DeclarationFact(
                symbol.SemanticId, symbol.Name, kind!.Value, AccessOf(symbol), IsImplicit(symbol), LocationOf(symbol), counted)
            {
                Role = role,
                // a parameter is a local of its procedure that no statement declares.
                DeclaredBy = symbol is not VBParameterSymbol && symbol is VBLocalVariableSymbol local ? local.DeclaredBy : null,
                Hides = hides?.Invoke(symbol),
                AutoInstantiationAlwaysFails = AutoInstantiationAlwaysFails(symbol),
            });
        }

        return facts.ToImmutable();
    }

    // a variable, or an array whose elements are, declared As New of a class whose objects are never created (Dim e As New ErrObject).
    private static bool AutoInstantiationAlwaysFails(Symbol symbol)
        => symbol.GetProperty(SymbolProperties.AutoInstantiated)
            && symbol is ITypedSymbol { ResolvedType: var type }
            && (type is VBArrayType array ? array.ItemType : type) is VBClassType { Symbol: var created }
            && created.GetProperty(SymbolProperties.CreationAlwaysFails);

    // a member that a form's designer declares on its class is declared by no text of the module. Its parameters are those of the form object's member, or of
    // the control library class's member, that it stands for (the value of a Property Let Width, named Width), and no code of the module declares them.
    public static IEnumerable<Symbol> DeclaredBy(IEnumerable<VBTypeMemberSymbol> members)
        => members.SelectMany(member => FormObject.IsDeclaredBy(member)
            ? new Symbol[] { member }
            : new Symbol[] { member }
                .Concat(DeclarationStaticSemanticsEvaluator.ParametersOf(member))
                .Concat(DeclarationStaticSemanticsEvaluator.LocalsOf(member)));

    // a local, a parameter, and a variable or a constant of the module that is not Public or Friend: nothing outside the module's code can refer to them, and
    // what refers to one by name is an expression. A procedure, a property or an event can be called with no expression that names it.
    private static bool IsAccessibleOnlyFromItsOwnCode(Symbol symbol, DeclarationKind kind) => kind switch
    {
        DeclarationKind.Parameter => true,
        DeclarationKind.Variable when symbol is VBLocalVariableSymbol => true,
        // a variable that is declared with Dim or with no modifier at all is Private.
        DeclarationKind.Variable => symbol is AccessibleTypedSymbol { AccessModifier: AccessModifier.Private or AccessModifier.Implicit },
        // a constant is referred to where its value is substituted, which includes the bounds of an array and the value of another constant, of an enumeration
        // member and of an optional parameter: expressions of the declarations of a module, which the pass does not evaluate as it does those of its procedures.
        // 🚧 TODO count the references of a constant once the expressions of declarations are evaluated.
        _ => false,
    };

    // a Private handler that does not match the event it is named for is no handler: no event calls it, and nothing outside the module can, so what calls it is an
    // expression of the module's code. A Public one may be called through the form's interface, by code the pass does not see.
    private static bool IsCalledOnlyByItsOwnCode(Symbol symbol, DeclarationRole role)
        => role is DeclarationRole.UnmatchedEventHandler && symbol is AccessibleTypedSymbol { AccessModifier: AccessModifier.Private };

    // the code that could refer to a local or a parameter is the code of its procedure; to anything else that is confined to the module, any procedure of it.
    private static bool CodeThatCanReferToIt(Symbol symbol, List<ProcedureSemanticModel> models, bool moduleIsFullyAnalyzed)
        => symbol is VBLocalVariableSymbol or VBLocalConstantSymbol
            ? models.Any(procedure => procedure.IsFullyAnalyzed && procedure.Procedure.Uri.AbsoluteUri == symbol.ParentUri.AbsoluteUri)
            : moduleIsFullyAnalyzed;

    private static DeclarationKind? KindOf(Symbol symbol) => symbol switch
    {
        VBParameterSymbol => DeclarationKind.Parameter,
        VBLocalVariableSymbol or VBModuleFieldVariableMemberSymbol => DeclarationKind.Variable,
        VBLocalConstantSymbol or VBConstantMemberSymbol => DeclarationKind.Constant,
        VBPropertyGetMemberSymbol or VBPropertyLetMemberSymbol or VBPropertySetMemberSymbol => DeclarationKind.Property,
        VBFunctionMemberSymbol or VBProcedureMemberSymbol => DeclarationKind.Procedure,
        VBEventMemberSymbol => DeclarationKind.Event,
        _ => null,
    };

    private static AccessModifier AccessOf(Symbol symbol)
        => symbol is AccessibleTypedSymbol accessible ? accessible.AccessModifier : AccessModifier.Private;

    private static bool IsImplicit(Symbol symbol) => symbol is VBLocalVariableSymbol { DeclaredBy: var kind } && kind.IsImplicit();

    private static SourceLocation LocationOf(Symbol symbol)
        => new(symbol.ParentUri, symbol switch
        {
            AccessibleTypedSymbol accessible => accessible.SelectionRange,
            BoundTypedSymbol bound => bound.SelectionRange,
            _ => SourceRange.Empty,
        });
}
