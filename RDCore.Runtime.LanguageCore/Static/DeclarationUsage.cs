using RDCore.SDK.Model;
using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.AST.Statements;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Semantics.Flags;
using RDCore.SDK.Workspace.Forms;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using RDCore.SDK.Semantics;
using ExpressionClassification = RDCore.SDK.Semantics.ExpressionClassification;

namespace RDCore.Runtime.LanguageCore.Static;

// a count is stated only when it is the whole truth: nothing outside the analyzed code can refer to the declaration, and that code was fully analyzed.
public static class DeclarationUsage
{
    // namesInUnanalyzedCode: the names written in the procedures of the module that were not fully analyzed (NamesIn), or null when they are not known.
    public static ImmutableArray<DeclarationFact> Of(
        IEnumerable<Symbol> declared, IEnumerable<ProcedureSemanticModel> procedures, IReadOnlyDictionary<SemanticId, DeclarationBinding>? bindings = null,
        Func<Symbol, HiddenDeclaration?>? hides = null, ArgumentParameters? arguments = null, ModuleDeclarationFacts? declarations = null,
        IReadOnlySet<string>? excludedBranchNames = null, Func<Symbol, ImmutableArray<CompiledPlatforms>?>? statementsCompiledOn = null,
        IEnumerable<string>? namesInUnanalyzedCode = null)
    {
        var models = procedures.ToList();
        var moduleIsFullyAnalyzed = models.All(procedure => procedure.IsFullyAnalyzed);
        var unanalyzedNames = namesInUnanalyzedCode?.Select(Normalized).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var code = CodeOf(models, declarations).ToList();
        var declaredList = declared.ToList();
        declared = declaredList;
        var parameters = declaredList.OfType<VBParameterSymbol>().Select(parameter => parameter.SemanticId).ToHashSet();
        var assigned = moduleIsFullyAnalyzed && arguments is not null
            ? AssignedParameters([.. code.SelectMany(each => Bound(each.Expressions))], arguments, parameters)
            : null;
        var references = Tally(code, fact => IsNeverAssigned(fact, arguments, parameters, assigned));

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

            var binding = bindings?.GetValueOrDefault(symbol.SemanticId);
            var role = binding?.Role ?? DeclarationRole.None;
            References? counted = null;
            // a name that a branch of a #If that is excluded writes can be a reference in another configuration (ExcludedBranchNames). A constant of the
            // module is also referred to by the expressions of the module's declarations: the facts of those are needed for its count.
            if ((IsAccessibleOnlyFromItsOwnCode(symbol, kind!.Value) || IsCalledOnlyByItsOwnCode(symbol, role))
                && (symbol is not VBConstantMemberSymbol || declarations is { IsFullyAnalyzed: true })
                && CodeThatCanReferToIt(symbol, models, moduleIsFullyAnalyzed, unanalyzedNames)
                && excludedBranchNames?.Contains(symbol.Name) != true)
            {
                counted = references.GetValueOrDefault(Key(symbol.SemanticId)) ?? new References();
            }

            // a procedure that several Declare statements declare is declared on every platform one of them is compiled on.
            var statements = symbol is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol ? statementsCompiledOn?.Invoke(symbol) : null;
            facts.Add(new DeclarationFact(
                symbol.SemanticId, symbol.Name, kind!.Value, AccessOf(symbol), IsImplicit(symbol), LocationOf(symbol), counted?.Count)
            {
                ReferencedFrom = counted?.Sites.ToImmutable(),
                Role = role,
                Binding = binding,
                // a parameter is a local of its procedure that no statement declares.
                DeclaredBy = symbol is not VBParameterSymbol && symbol is VBLocalVariableSymbol local ? local.DeclaredBy : null,
                Hides = hides?.Invoke(symbol),
                AutoInstantiates = ClassFacts.AutoInstantiatedClassOf(symbol)?.SemanticId,
                CompiledOn = statements?.Aggregate(CompiledPlatforms.None, (all, statement) => all | statement),
                StatementsCompiledOn = statements ?? default,
                TypeKind = TypeKindOf(symbol),
                IsArray = symbol is ITypedSymbol { ResolvedType: VBArrayType },
            });
        }

        return facts.ToImmutable();
    }

    // MS-VBAL §5.2.3.1.4, §5.2.3.1.5: the type a declaration is declared as, of its elements for an array; what a function or a property returns. A Sub and an
    // Event have none (Void), and a type that did not resolve is not known.
    private static DeclaredTypeKind? TypeKindOf(Symbol symbol)
        => (symbol is ITypedSymbol { ResolvedType: var type } ? type is VBArrayType array ? array.ItemType : type : null) switch
        {
            VBVariantType => DeclaredTypeKind.Variant,
            VBObjectType => DeclaredTypeKind.Object,
            VBClassType => DeclaredTypeKind.Class,
            VBUserDefinedType => DeclaredTypeKind.UserDefinedType,
            VBEnumType => DeclaredTypeKind.Enum,
            VBIntrinsicType => DeclaredTypeKind.Intrinsic,
            _ => null,
        };

    // MS-VBAL §5.3.1.11: an argument passed by reference is assigned when the procedure writes to its parameter, or passes the parameter on by reference to
    // one that does. The parameters of the module's own procedures are followed to a fixed point, which a procedure that calls itself does not change. A
    // parameter passed on where no parameter of the module is known to be given it (a procedure of another module or of a library, a Declare, a call bound
    // late) may be assigned.
    private static HashSet<SemanticId> AssignedParameters(List<ExpressionFact> bound, ArgumentParameters arguments, HashSet<SemanticId> parameters)
    {
        var assigned = new HashSet<SemanticId>();
        var passedOn = new List<(SemanticId From, SemanticId To)>();
        foreach (var fact in bound.Where(fact => parameters.Contains(fact.Binding!.Value)))
        {
            if (fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget))
            {
                assigned.Add(fact.Binding!.Value);
            }
            else if (fact.Flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument))
            {
                if (arguments.TryGet(fact.Node, out var to) && parameters.Contains(to))
                {
                    passedOn.Add((fact.Binding!.Value, to));
                }
                else
                {
                    assigned.Add(fact.Binding!.Value);
                }
            }
        }

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (from, to) in passedOn)
            {
                changed |= assigned.Contains(to) && assigned.Add(from);
            }
        }

        return assigned;
    }

    // an argument given by reference to a parameter of one of the module's own procedures that nothing assigns is read by the call, and not written.
    private static bool IsNeverAssigned(ExpressionFact fact, ArgumentParameters? arguments, HashSet<SemanticId> parameters, HashSet<SemanticId>? assigned)
        => assigned is not null && arguments is not null && arguments.TryGet(fact.Node, out var parameter)
            && parameters.Contains(parameter) && !assigned.Contains(parameter);

    // MS-VBAL §5.3.1.1, §5.2.1.4: a Friend procedure, and a Public variable or constant of a procedural module that is Option Private Module, are accessible
    // within the project and from nowhere else - the projects that reference it included. Every reference to one is in the code of a module of the project, and
    // it is counted over all of them once each module has a model. A procedure that was not fully analyzed refers to a declaration by writing its name
    // (MS-VBAL §5.6.10): a declaration of a name one writes (NamesIn) is not counted, and the rest are. unanalyzedNames holds those names for each module, by
    // the module's address; when a module has a procedure not fully analyzed and its names are not known, nothing is counted. A constant is counted once the
    // expressions in the declarations of each module were analyzed too (the value of another constant, the bounds of an array). A Public procedure of a
    // private module is not counted here: its name can be passed as a string to a library member that calls a procedure by its name, which a count of the
    // expressions does not see. Nor is a declaration of a name that an excluded #If branch of a module of the project writes (ExcludedBranchNames):
    // excludedBranchNames holds those names for each module, by the module's address.
    public static IReadOnlyCollection<ModuleSemanticModel> AcrossProject(
        IReadOnlyCollection<ModuleSemanticModel> models, IReadOnlyDictionary<string, ModuleDeclarationFacts> declarations, IEnumerable<VBModuleSymbol> project,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? excludedBranchNames = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? unanalyzedNames = null)
    {
        var projectModules = project.DistinctBy(module => module.Uri.AbsoluteUri).ToList();
        var modules = projectModules.Select(module => module.Uri.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
        var privateModules = projectModules.Where(module => module is not VBClassModuleSymbol && module.Directives.PrivateModule)
            .Select(module => module.Uri.AbsoluteUri).ToHashSet(StringComparer.Ordinal);
        // MS-VBAL §5.2.4.2: a class that the Implements directive of a module of the project names is implemented, whether or not the code is analyzed.
        var implemented = projectModules.OfType<VBClassModuleSymbol>().SelectMany(module => module.ImplementedInterfaces)
            .Select(implementedClass => implementedClass.Uri.AbsoluteUri).Where(modules.Contains).ToHashSet(StringComparer.Ordinal);
        models = [.. models.Select(model => model.Class is { } flags && implemented.Contains(model.Module.AbsoluteUri)
            ? model with { Class = flags | ClassModuleSemanticFlags.Implemented }
            : model)];

        var inProject = models.Where(model => modules.Contains(model.Module.AbsoluteUri)).ToList();
        if (modules.Count == 0
            || inProject.Select(model => model.Module.AbsoluteUri).Distinct(StringComparer.Ordinal).Count() < modules.Count)
        {
            return models;
        }

        var unanalyzed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in inProject.Where(model => !model.Procedures.All(procedure => procedure.IsFullyAnalyzed)))
        {
            if (unanalyzedNames?.GetValueOrDefault(model.Module.AbsoluteUri) is not { } names)
            {
                return models;
            }

            unanalyzed.UnionWith(names.Select(Normalized));
        }

        var constantsAreFullyAnalyzed = modules.All(module => declarations.TryGetValue(module, out var facts) && facts.IsFullyAnalyzed);
        var references = Tally(inProject.SelectMany(model => CodeOf(model.Procedures, declarations.GetValueOrDefault(model.Module.AbsoluteUri))));
        var excluded = modules.SelectMany(module => excludedBranchNames?.GetValueOrDefault(module) ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. models.Select(model => !modules.Contains(model.Module.AbsoluteUri) ? model : model with
        {
            Declarations = [.. model.Declarations.Select(fact => fact.References is null
                && IsAccessibleOnlyWithinTheProject(fact, privateModules.Contains(model.Module.AbsoluteUri), constantsAreFullyAnalyzed)
                && !excluded.Contains(fact.Name)
                && !unanalyzed.Contains(Normalized(fact.Name))
                    ? Counted(fact, references.GetValueOrDefault(Key(fact.Symbol)) ?? new References())
                    : fact)],
        })];
    }

    private static bool IsAccessibleOnlyWithinTheProject(DeclarationFact fact, bool inPrivateModule, bool constantsAreFullyAnalyzed) => fact switch
    {
        { Role: not DeclarationRole.None } => false,
        { Kind: DeclarationKind.Procedure or DeclarationKind.Property, Access: AccessModifier.Friend } => true,
        { Kind: DeclarationKind.Variable, Access: AccessModifier.Public or AccessModifier.Global } => inPrivateModule,
        { Kind: DeclarationKind.Constant, Access: AccessModifier.Public or AccessModifier.Global } => inPrivateModule && constantsAreFullyAnalyzed,
        _ => false,
    };

    private static DeclarationFact Counted(DeclarationFact fact, References references)
        => fact with { References = references.Count, ReferencedFrom = references.Sites.ToImmutable() };

    // what refers to each declaration, by its key: the expressions of the procedures that are bound to it, counted by what they do.
    private sealed class References
    {
        public DeclarationReferences Count { get; set; }

        public ImmutableArray<DeclarationReference>.Builder Sites { get; } = ImmutableArray.CreateBuilder<DeclarationReference>();
    }

    // code that refers to declarations: the body of a procedure, or the declarations of a module, which no procedure holds.
    private readonly record struct Code(SemanticId? Procedure, IEnumerable<ExpressionFact> Expressions);

    private static IEnumerable<Code> CodeOf(IEnumerable<ProcedureSemanticModel> procedures, ModuleDeclarationFacts? declarations)
        => procedures.Select(procedure => new Code(procedure.Procedure, procedure.Expressions.Values))
            .Concat(declarations is null ? Enumerable.Empty<Code>() : [new Code(null, declarations.Expressions.Values)]);

    // a reference is a place in the code: the tree can hold an expression twice (the bound of an array of the module is also a child of its declaration),
    // and both are evaluated, as what the tree holds is.
    private static IEnumerable<ExpressionFact> Bound(IEnumerable<ExpressionFact> expressions)
        => expressions.Where(fact => fact.Binding is not null).DistinctBy(fact => (fact.Location, fact.Binding));

    // isReadByTheCall: an argument that the call reads and does not write, though it is passed by reference (IsNeverAssigned).
    private static Dictionary<string, References> Tally(IEnumerable<Code> code, Func<ExpressionFact, bool>? isReadByTheCall = null)
    {
        var references = new Dictionary<string, References>(StringComparer.Ordinal);
        foreach (var (procedure, expressions) in code)
        {
            var bound = Bound(expressions)
                .GroupBy(fact => (Key: Key(fact.Binding!.Value), fact.Location.Range.Start))
                .SelectMany(group => group.Any(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.ProcedureCall))
                    ? Call(procedure, [.. group])
                    : group.Select(fact => (Fact: fact, Kind: KindOf(fact, isReadByTheCall), fact.Flags)))
                .OrderBy(reference => reference.Fact.Location.Range.Start);
            foreach (var (fact, kind, flags) in bound)
            {
                var key = Key(fact.Binding!.Value);
                if (!references.TryGetValue(key, out var found))
                {
                    references[key] = found = new References();
                }

                var current = found.Count;
                found.Count = kind switch
                {
                    DeclarationReferenceKind.Write => current with { Writes = current.Writes + 1 },
                    DeclarationReferenceKind.PassedAsArgument => current with { PassedAsArguments = current.PassedAsArguments + 1 },
                    _ => current with { Reads = current.Reads + 1 },
                };
                found.Sites.Add(new DeclarationReference(fact.Location, procedure?.Uri, kind)
                {
                    DiscardsResult = flags.HasFlag(ValueExpressionSemanticFlags.ResultDiscarded),
                });
            }
        }

        return references;
    }

    private static DeclarationReferenceKind KindOf(ExpressionFact fact, Func<ExpressionFact, bool>? isReadByTheCall) => fact.Flags switch
    {
        var flags when flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) => DeclarationReferenceKind.Write,
        var flags when flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument) && isReadByTheCall?.Invoke(fact) != true
            => DeclarationReferenceKind.PassedAsArgument,
        _ => DeclarationReferenceKind.Read,
    };

    // a call written with an argument list is two expressions bound to what it calls, the name and the call (`Compute(1)`): one reference, at the name, that
    // reads it or, for a Property Let or Set, writes it. MS-VBAL §5.3.1, §5.6.2.1: in a Function or a Property Get, its own name with no argument list is its
    // function result variable, which no other code can refer to - and not a reference to the procedure.
    private static IEnumerable<(ExpressionFact Fact, DeclarationReferenceKind Kind, ValueExpressionSemanticFlags Flags)> Call(
        SemanticId? procedure, ExpressionFact[] call)
    {
        var name = call.MinBy(fact => fact.Location.Range.End)!;
        if (call.Length == 1 && procedure is not null && name.Binding == procedure
            && name.Classification is ExpressionClassification.Function or ExpressionClassification.Property)
        {
            yield break;
        }

        var flags = call.Aggregate(default(ValueExpressionSemanticFlags), (all, fact) => all | fact.Flags);
        yield return (name, flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) ? DeclarationReferenceKind.Write : DeclarationReferenceKind.Read, flags);
    }

    // the accessors of a property are one declaration: a Property Let or Property Set is addressed with a suffix after the name it shares with the others
    // (`Item]Let`). VBA names are not case sensitive. A Let and a Set are both Writes of the one count, so the count cannot tell which accessor is never used,
    // and the Get, the Let and the Set each state the property's whole count.
    private static string Key(SemanticId symbol)
    {
        var address = Uri.UnescapeDataString(symbol.Uri.AbsoluteUri).ToUpperInvariant();
        return address.EndsWith("]LET", StringComparison.Ordinal) || address.EndsWith("]SET", StringComparison.Ordinal) ? address[..^4] : address;
    }

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
        // a constant of a procedure is referred to by that procedure's code alone: its statements, and the declarations of its body - the value of another of
        // its constants, the bounds of its arrays - which are analyzed with the statements.
        DeclarationKind.Constant when symbol is VBLocalConstantSymbol => true,
        // a constant of the module is referred to where its value is substituted: by the code of the module, and by the expressions of its declarations - the
        // bounds of an array, the value of another constant, of an enumeration member and of an optional parameter of a Declare.
        DeclarationKind.Constant => symbol is AccessibleTypedSymbol { AccessModifier: AccessModifier.Private or AccessModifier.Implicit },
        _ => false,
    };

    // a Private handler that does not match the event it is named for is no handler: no event calls it, and nothing outside the module can, so what calls it is an
    // expression of the module's code. A Public one may be called through the form's interface, by code the pass does not see.
    private static bool IsCalledOnlyByItsOwnCode(Symbol symbol, DeclarationRole role)
        => role is DeclarationRole.UnmatchedEventHandler && symbol is AccessibleTypedSymbol { AccessModifier: AccessModifier.Private };

    // the code that could refer to a local or a parameter is the code of its procedure; to anything else that is confined to the module, any procedure of it.
    // A procedure refers to a declaration of the module by writing its name (MS-VBAL §5.6.10): one that was not fully analyzed, and that writes no name the
    // declaration has, does not refer to it, and the rest of the module is the code that can.
    private static bool CodeThatCanReferToIt(
        Symbol symbol, List<ProcedureSemanticModel> models, bool moduleIsFullyAnalyzed, IReadOnlySet<string>? unanalyzedNames)
        => symbol is VBLocalVariableSymbol or VBLocalConstantSymbol
            ? models.Any(procedure => procedure.IsFullyAnalyzed && procedure.Procedure.Uri.AbsoluteUri == symbol.ParentUri.AbsoluteUri)
            : moduleIsFullyAnalyzed || unanalyzedNames is not null && !unanalyzedNames.Contains(Normalized(symbol.Name));

    /// <summary>
    /// Every name that <paramref name="procedure"/> writes: the names of its expressions and of the members they access, and each word of what the parser
    /// could not read. What it holds is taken as written wherever it is, so the names are as many as can refer to something, and more.
    /// </summary>
    public static IReadOnlySet<string> NamesIn(SyntaxNode procedure)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<SyntaxNode>();
        pending.Push(procedure);
        while (pending.TryPop(out var node))
        {
            if (!seen.Add(node))
            {
                continue;
            }

            switch (node)
            {
                case SimpleNameExpressionNode name:
                    names.Add(Normalized(name.IdentifierName));
                    break;
                case VBTypedDeclarationExpressionNode typed:
                    names.Add(Normalized(typed.IdentifierName));
                    break;
                case RedimDeclarationNode redim:
                    names.Add(Normalized(redim.Name));
                    break;
                case UnbuiltStatementTriviaNode statement:
                    names.UnionWith(WordsOf(statement.Source));
                    break;
                case UnbuiltExpressionTriviaNode expression:
                    names.UnionWith(WordsOf(expression.Source));
                    break;
            }

            foreach (var child in ReachedFrom(node))
            {
                pending.Push(child);
            }
        }

        return names;
    }

    // what a node holds that its children may not include: what a statement assigns or redimensions, the member an access names, the procedure AddressOf
    // gives, the bounds of an array, and the bodies of a block statement.
    private static IEnumerable<SyntaxNode> ReachedFrom(SyntaxNode node)
    {
        IEnumerable<SyntaxNode?> held = node switch
        {
            RedimDeclarationNode redim => [redim.Target],
            AssignmentStatementNode assignment => [assignment.Target, assignment.Value],
            MidStatementNode mid => [mid.Target],
            MemberAccessExpressionNode access => [access.Owner, access.Member],
            DictionaryAccessExpressionNode access => [access.Owner, access.Member],
            AddressOfExpressionNode addressOf => [addressOf.Target],
            ArrayBoundsNode bounds => bounds.Bounds.IsDefault ? [] : bounds.Bounds.SelectMany(bound => new SyntaxNode?[] { bound.LowerExpression, bound.UpperExpression }),
            StatementNode statement => ExpressionCoverage.NestedBlocksOf(statement).SelectMany(block => block.Children),
            _ => [],
        };

        return node.ChildNodes().Concat(held.OfType<SyntaxNode>());
    }

    // an identifier is a letter and the letters, digits and underscores after it; one in square brackets is anything but a bracket.
    private static IEnumerable<string> WordsOf(string? source)
        => string.IsNullOrEmpty(source)
            ? []
            : IdentifierPattern.Matches(source).Select(match => Normalized(match.Value));

    private static readonly Regex IdentifierPattern = new(@"\[[^\]\r\n]*\]|\p{L}[\p{L}\p{Nd}_]*", RegexOptions.CultureInvariant);

    // a name without the brackets it can be written in, nor the type-declaration character it can be written with (MS-VBAL §3.3.5.3).
    private static string Normalized(string name) => name.Trim('[', ']').TrimEnd('%', '&', '^', '!', '#', '@', '$');

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

    // a local that no statement declares, or a variable of the module that a name declares by being written (SymbolBuilder.BuildImplicitModuleVariable).
    private static bool IsImplicit(Symbol symbol) => symbol is VBLocalVariableSymbol { DeclaredBy: var kind } && kind.IsImplicit()
        || symbol.GetProperty(SymbolProperties.ImplicitlyDeclared);

    private static SourceLocation LocationOf(Symbol symbol)
        => new(symbol.ParentUri, symbol switch
        {
            AccessibleTypedSymbol accessible => accessible.SelectionRange,
            BoundTypedSymbol bound => bound.SelectionRange,
            _ => SourceRange.Empty,
        });
}
