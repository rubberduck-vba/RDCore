using RDCore.Runtime.LanguageCore.Symbols;
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
    // byRefArguments: what the calls of the module's code do with the arguments they take by reference (ArgumentsOf), or null when that is not known.
    public static ImmutableArray<DeclarationFact> Of(
        IEnumerable<Symbol> declared, IEnumerable<ProcedureSemanticModel> procedures, IReadOnlyDictionary<SemanticId, DeclarationBinding>? bindings = null,
        Func<Symbol, HiddenDeclaration?>? hides = null, ByRefArguments? byRefArguments = null, ModuleDeclarationFacts? declarations = null,
        IReadOnlySet<string>? excludedBranchNames = null, Func<Symbol, ImmutableArray<CompiledPlatforms>?>? statementsCompiledOn = null,
        IEnumerable<string>? namesInUnanalyzedCode = null)
    {
        var models = procedures.ToList();
        var moduleIsFullyAnalyzed = models.All(procedure => procedure.IsFullyAnalyzed);
        var unanalyzedNames = namesInUnanalyzedCode?.Select(Normalized).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var references = Tally(CodeOf(models, declarations, byRefArguments));

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

    /// <summary>
    /// What the calls of a module's code do with the arguments they take by reference, where the procedure called is one of the module's own: those each
    /// call is known to assign, whatever the rest of the module is, and those it only reads, once every procedure of the module was fully analyzed. What
    /// the module's code does with its parameters and passes on goes with them, for a count across the project to follow (<see cref="ByRefFlow"/>).
    /// </summary>
    /// <param name="declared">What the module declares, its procedures' parameters included (<see cref="DeclaredBy"/>).</param>
    /// <param name="procedures">The models of the module's procedures.</param>
    /// <param name="arguments">The parameter each argument of the module's code is given, where its procedure is the module's own.</param>
    public static ByRefArguments ArgumentsOf(IEnumerable<Symbol> declared, IEnumerable<ProcedureSemanticModel> procedures, ArgumentParameters arguments)
    {
        var models = procedures.ToList();
        // a Declare'd procedure has no body of the module's: what its library does with a parameter is not known, and an argument given to one may be
        // assigned.
        var bodiless = declared.OfType<VBTypeMemberSymbol>()
            .Where(member => member is VBExternalFunctionMemberSymbol or VBExternalSubMemberSymbol)
            .SelectMany(member => DeclarationStaticSemanticsEvaluator.ParametersOf(member))
            .Select(parameter => parameter.SemanticId)
            .ToHashSet();
        var parameters = declared.OfType<VBParameterSymbol>().Select(parameter => parameter.SemanticId).Where(id => !bodiless.Contains(id)).ToHashSet();
        var bound = Bound(models.SelectMany(procedure => procedure.Expressions.Values)).ToList();
        var given = models.SelectMany(procedure => procedure.Arguments)
            .Where(argument => argument is { IsExternal: false, IsLibrary: false, IsParamArray: false })
            .DistinctBy(argument => argument.Argument)
            .ToDictionary(argument => argument.Argument, argument => new SemanticId(argument.Parameter));

        var passed = new List<ByRefArgument>();
        foreach (var fact in bound.Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument)))
        {
            var own = arguments.TryGet(fact.Node, out var parameter) && parameters.Contains(parameter);
            passed.Add(new ByRefArgument(
                fact.Location.Range, fact.Binding!.Value, own ? parameter : given.TryGetValue(fact.Node, out var other) ? other : null, own));
        }

        var assigned = bound.Where(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) && parameters.Contains(fact.Binding!.Value))
            .Select(fact => fact.Binding!.Value)
            .ToHashSet();
        var flow = new ByRefFlow(parameters, assigned, passed, models.All(procedure => procedure.IsFullyAnalyzed));
        return Followed([flow], argument => argument.CallsItsOwnProcedure)[0] with { Flow = flow };
    }

    // MS-VBAL §5.3.1.11: an argument passed by reference is assigned when the procedure writes to its parameter, or passes the parameter on by reference to
    // one that does. The parameters that the calls followed give arguments to are followed to a fixed point, which a procedure that calls itself does not
    // change. A parameter passed on to a call that is not followed (through a variable of a class, to a library, a Declare, a call bound late, or one of a
    // module that has no flow), or one of a module whose code was not fully analyzed, may be assigned: it is a possible assignment, and not a known one.
    private static List<ByRefArguments> Followed(IReadOnlyList<ByRefFlow> flows, Func<ByRefArgument, bool> follows)
    {
        var parameters = flows.SelectMany(flow => flow.Parameters).ToHashSet();
        var known = flows.SelectMany(flow => flow.Assigned).ToHashSet();
        var possible = flows.SelectMany(flow => flow.IsFullyAnalyzed ? flow.Assigned : flow.Parameters).ToHashSet();
        bool IsFollowed(ByRefArgument argument) => argument.Parameter is { } to && parameters.Contains(to) && follows(argument);

        var passedOn = new List<(SemanticId From, SemanticId To)>();
        foreach (var argument in flows.SelectMany(flow => flow.Arguments.Where(argument => flow.Parameters.Contains(argument.Binding))))
        {
            if (IsFollowed(argument))
            {
                passedOn.Add((argument.Binding, argument.Parameter!.Value));
            }
            else
            {
                possible.Add(argument.Binding);
            }
        }

        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (from, to) in passedOn)
            {
                changed |= known.Contains(to) && known.Add(from);
                changed |= possible.Contains(to) && possible.Add(from);
            }
        }

        return [.. flows.Select(flow => new ByRefArguments(
            flow.Arguments.Where(argument => IsFollowed(argument) && known.Contains(argument.Parameter!.Value))
                .Select(argument => argument.Range).ToHashSet(),
            flow.Arguments.Where(argument => IsFollowed(argument) && !possible.Contains(argument.Parameter!.Value))
                .Select(argument => argument.Range).ToHashSet()))];
    }

    // MS-VBAL §5.3.1.1, §5.2.1.4: a Friend procedure, and a Public variable or constant of a procedural module that is Option Private Module, are accessible
    // within the project and from nowhere else - the projects that reference it included. Every reference to one is in the code of a module of the project, and
    // it is counted over all of them once each module has a model. A procedure that was not fully analyzed refers to a declaration by writing its name
    // (MS-VBAL §5.6.10): a declaration of a name such a procedure writes (NamesIn) is not counted, and the rest are. unanalyzedNames holds those names
    // for each module, by the module's address; when a module has a procedure not fully analyzed and its names are not known, nothing is counted.
    // A constant is counted once the
    // expressions in the declarations of each module were analyzed too (the value of another constant, the bounds of an array). A Public procedure of a
    // private module is not counted here: its name can be passed as a string to a library member that calls a procedure by its name, which a count of the
    // expressions does not see. Nor is a declaration of a name that an excluded #If branch of a module of the project writes (ExcludedBranchNames):
    // excludedBranchNames holds those names for each module, by the module's address. The procedure a run of the project starts with (entryPoint,
    // MemberRoles.EntryPointOf) is one only for the project as a whole, and is said here, whether or not the code is analyzed.
    // A program (a project that is not a library, whose entry point is found) is run from its entry point, and no other project nor any host calls into it:
    // a Public member of any of its modules is counted as a Friend one is (PublicMembersOfAProgram), with what each module's code accesses late (lateBound,
    // by the module's address). A library's members that a referencing project can use count as used by one (isLibrary,
    // DeclarationFact.IsUsedByReferencingProjects), and are not counted, except a class's that is not exposed when the library hands out no object
    // (HandsOutObjects), which are counted as a program's are. An argument a call takes by reference is followed into the procedure called where
    // the module's own count follows it, and into a procedure of a standard module of the project, which is the one a call of its name runs from any module
    // of the project (byRefArguments, by the module's address, ByRefFlow): for the declarations each module's own count counted as well, once every module
    // has a model. A member of a class called through a variable may run another class's that implements it, and is not followed.
    public static IReadOnlyCollection<ModuleSemanticModel> AcrossProject(
        IReadOnlyCollection<ModuleSemanticModel> models, IReadOnlyDictionary<string, ModuleDeclarationFacts> declarations, IEnumerable<VBModuleSymbol> project,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? excludedBranchNames = null, SemanticId? entryPoint = null, bool isLibrary = false,
        IReadOnlyDictionary<string, LateBoundNames>? lateBound = null, Func<VBModuleSymbol, IEnumerable<VBTypeMemberSymbol>>? membersOf = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? unanalyzedNames = null, IReadOnlyDictionary<string, ByRefArguments>? byRefArguments = null)
    {
        // the members of a module as the session's symbols have them: a standard module's symbol does not hold its own.
        membersOf ??= module => module.Members;
        if (entryPoint is { } start)
        {
            models = [.. models.Select(model => model.Declarations.Any(fact => fact.Symbol == start)
                ? model with
                {
                    Declarations = [.. model.Declarations.Select(fact => fact.Symbol == start ? fact with { Role = DeclarationRole.EntryPoint } : fact)],
                }
                : model)];
        }

        var projectModules = project.DistinctBy(module => module.Uri.AbsoluteUri).ToList();
        // what a referencing project can name, or reach through an object of its class, is said whether or not the code is analyzed. An object of a class
        // that is not exposed reaches a referencing project only through what the library gives it (HandsOutObjects).
        var closed = isLibrary && !HandsOutObjects(projectModules, membersOf);
        if (isLibrary)
        {
            var usedFromOutside = projectModules
                .Where(module => !closed || ReferencedProjectSymbols.IsAccessible(module))
                .SelectMany(module => membersOf(module).Where(member => ReferencedProjectSymbols.IsAccessible(member, module)))
                .Select(member => member.SemanticId)
                .ToHashSet();
            models = [.. models.Select(model => model.Declarations.Any(fact => usedFromOutside.Contains(fact.Symbol))
                ? model with
                {
                    Declarations = [.. model.Declarations.Select(fact => usedFromOutside.Contains(fact.Symbol)
                        ? fact with { IsUsedByReferencingProjects = true }
                        : fact)],
                }
                : model)];
        }

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

        var flows = projectModules.Select(module => (Module: module, Flow: byRefArguments?.GetValueOrDefault(module.Uri.AbsoluteUri)?.Flow))
            .Where(entry => entry.Flow is not null)
            .ToList();
        var standard = flows.Where(entry => entry.Module is VBStandardModuleSymbol).SelectMany(entry => entry.Flow!.Parameters).ToHashSet();
        var followed = Followed(
                [.. flows.Select(entry => entry.Flow!)], argument => argument.CallsItsOwnProcedure || standard.Contains(argument.Parameter!.Value))
            .Zip(flows, (arguments, entry) => (Arguments: arguments, entry.Module))
            .ToDictionary(entry => entry.Module.Uri.AbsoluteUri, entry => entry.Arguments, StringComparer.Ordinal);
        models = [.. models.Select(model => followed.TryGetValue(model.Module.AbsoluteUri, out var byRef)
            ? model with { Declarations = [.. model.Declarations.Select(fact => Followed(fact, byRef))] }
            : model)];
        // every module of the project is there: what the members bound to an interface member or an event refer to is said of its parameters.
        models = BoundParameters.Stated(models, projectModules, membersOf, isLibrary);
        inProject = [.. models.Where(model => modules.Contains(model.Module.AbsoluteUri))];

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
        var references = Tally(inProject.SelectMany(model => CodeOf(model.Procedures, declarations.GetValueOrDefault(model.Module.AbsoluteUri),
            followed.GetValueOrDefault(model.Module.AbsoluteUri) ?? byRefArguments?.GetValueOrDefault(model.Module.AbsoluteUri))));
        var excluded = modules.SelectMany(module => excludedBranchNames?.GetValueOrDefault(module) ?? Enumerable.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var program = !isLibrary && entryPoint is not null ? PublicMembersOfAProgram(projectModules, inProject, lateBound, membersOf, _ => true)
            : closed ? PublicMembersOfAProgram(projectModules, inProject, lateBound, membersOf, module => !ReferencedProjectSymbols.IsAccessible(module))
            : null;
        var late = projectModules.Select(module => lateBound?.GetValueOrDefault(module.Uri.AbsoluteUri)).ToList();
        var namedInStrings = late.All(names => names is not null)
            ? late.SelectMany(names => names!.NamedInStrings).Select(Normalized).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        return [.. models.Select(model => !modules.Contains(model.Module.AbsoluteUri) ? model : model with
        {
            Declarations = [.. model.Declarations.Select(fact => fact.References is null
                && (IsAccessibleOnlyWithinTheProject(fact, privateModules.Contains(model.Module.AbsoluteUri), constantsAreFullyAnalyzed)
                    || program?.Contains(fact.Symbol) == true && (fact.Kind is not DeclarationKind.Constant || constantsAreFullyAnalyzed)
                    || IsCalledOnlyByItsModule(fact, model, lateBound?.GetValueOrDefault(model.Module.AbsoluteUri), namedInStrings))
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

    // MS-VBAL §5.3.1.1: a Private procedure or property is accessible within its module and from nowhere else, and is not reached late: every call of one
    // is an expression of its module. One with a role (a handler, an implementation) is called by no expression, nor is one of a module that calls
    // CallByName with a name no string literal gives (LateBoundNames.CallsByName), nor one of a document module whose host's class is not known, whose events
    // are not either. A host can call back a procedure by the name a string gives it (Application.Run, OnAction, OnTime), and a project's code says
    // nothing of the host's members that do: a Private procedure whose name a string literal of the project writes (LateBoundNames.NamedInStrings) is not
    // counted. namedInStrings holds those names, when the strings of every module of the project are known.
    private static bool IsCalledOnlyByItsModule(DeclarationFact fact, ModuleSemanticModel model, LateBoundNames? late, IReadOnlySet<string>? namedInStrings)
        => fact is { Role: DeclarationRole.None, Kind: DeclarationKind.Procedure or DeclarationKind.Property, Access: AccessModifier.Private }
            && late is { CallsByName: false }
            && namedInStrings is not null && !namedInStrings.Contains(Normalized(fact.Name))
            && model.Document is not { HostClassUri: null };

    // the Public procedures, properties, variables and constants of a program's modules (a procedure or a property with no access modifier is Public,
    // MS-VBAL §5.3.1.1), that only the program's own code can call. The implementation of an interface member and the handler of an event are counted too:
    // the interface and the event call them through no expression of theirs, so what is counted is the code that calls them by their own name. The entry
    // point and a handler with no event to handle are not. A member of a class can also be reached where the code runs: as the default member or the
    // enumerator of its object (VB_UserMemId 0 and -4, MS-VBAL §5.6.2.2, §5.4.2.4), through an access bound late that names it, or by CallByName
    // (LateBoundNames). Those are not counted: no member of a class is, when the program calls CallByName, or when the late accesses of a module are not
    // known. A library that hands out no object counts those of its classes that are not exposed the same way (counts, the modules whose members are
    // counted): only its own code can reach them.
    private static HashSet<SemanticId> PublicMembersOfAProgram(
        List<VBModuleSymbol> project, List<ModuleSemanticModel> models, IReadOnlyDictionary<string, LateBoundNames>? lateBound,
        Func<VBModuleSymbol, IEnumerable<VBTypeMemberSymbol>> membersOf, Func<VBModuleSymbol, bool> counts)
    {
        var late = project.Select(module => lateBound?.GetValueOrDefault(module.Uri.AbsoluteUri)).ToList();
        var classMembersAreCounted = late.All(names => names is { CallsByName: false });
        var namedLate = late.OfType<LateBoundNames>().SelectMany(names => names.Members).Select(Normalized).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roles = models.SelectMany(model => model.Declarations)
            .Where(fact => fact.Role is not (DeclarationRole.None or DeclarationRole.InterfaceImplementation or DeclarationRole.EventHandler))
            .Select(fact => fact.Symbol)
            .ToHashSet();

        return [.. project.Where(counts).SelectMany(module => membersOf(module)
            .Where(member => member is AccessibleTypedSymbol { AccessModifier: var access } && KindOf(member) is { } kind && access switch
            {
                AccessModifier.Public or AccessModifier.Global => kind is not DeclarationKind.Event,
                AccessModifier.Implicit => kind is DeclarationKind.Procedure or DeclarationKind.Property,
                _ => false,
            })
            .Where(member => !roles.Contains(member.SemanticId))
            .Where(member => module is not VBClassModuleSymbol
                || classMembersAreCounted && !namedLate.Contains(Normalized(member.Name))
                    && !(member.TryGetProperty(SymbolProperties.UserMemId, out var id) && id is WellKnownDispIds.Value or WellKnownDispIds.NewEnum)))
            .Select(member => member.SemanticId)];
    }

    // MS-VBAL §5.2.4.1.1: a referencing project cannot name a class that is not exposed, and holds an object of it only as a library gives it one: as what
    // a member that the project can use (ReferencedProjectSymbols.IsAccessible) is declared as or returns, or as an argument of one, an event's included,
    // given to its own code. Each can carry one when it is declared as a Variant, an Object or a class, or as a user-defined type with such a field; and so
    // can one whose type did not resolve.
    private static bool HandsOutObjects(List<VBModuleSymbol> project, Func<VBModuleSymbol, IEnumerable<VBTypeMemberSymbol>> membersOf)
        => project.Where(ReferencedProjectSymbols.IsAccessible)
            .SelectMany(module => membersOf(module).Where(member => ReferencedProjectSymbols.IsAccessible(member, module)))
            .SelectMany(member => DeclarationStaticSemanticsEvaluator.ParametersOf(member)
                .Where(parameter => !(parameter.Name == "Me" && parameter.ParameterKind == ParameterKind.ImplicitByRef))
                .Cast<Symbol>()
                .Prepend(member))
            .Any(symbol => symbol is ITypedSymbol typed && (typed.ResolvedType is not { } type || CanHoldAnObject(type, [])));

    private static bool CanHoldAnObject(VBType type, HashSet<VBType> seen) => type switch
    {
        VBArrayType array => CanHoldAnObject(array.ItemType, seen),
        VBVariantType or VBObjectType or VBClassType => true,
        VBUserDefinedType record => seen.Add(record) && record.Members.Any(field => field is not ITypedSymbol { ResolvedType: { } fieldType }
            || CanHoldAnObject(fieldType, seen)),
        _ => false,
    };

    private static DeclarationFact Counted(DeclarationFact fact, References references)
        => fact with { References = references.Count, ReferencedFrom = references.Sites.ToImmutable() };

    // a declaration that its module's own count counted, as a count across the project follows its arguments (ByRefArguments): one that the call only reads
    // is read, and one that the call is known to assign is assigned through it.
    private static DeclarationFact Followed(DeclarationFact fact, ByRefArguments byRef)
    {
        if (fact is not { References: { } count, ReferencedFrom: { IsDefault: false } sites }
            || !sites.Any(site => site.Kind is DeclarationReferenceKind.PassedAsArgument
                && (byRef.Read.Contains(site.Location.Range) || !site.AssignedThroughCall && byRef.Assigned.Contains(site.Location.Range))))
        {
            return fact;
        }

        var read = sites.Count(site => site.Kind is DeclarationReferenceKind.PassedAsArgument && byRef.Read.Contains(site.Location.Range));
        return fact with
        {
            References = count with { Reads = count.Reads + read, PassedAsArguments = count.PassedAsArguments - read },
            ReferencedFrom = [.. sites.Select(site => site.Kind is not DeclarationReferenceKind.PassedAsArgument ? site
                : byRef.Read.Contains(site.Location.Range) ? site with { Kind = DeclarationReferenceKind.Read }
                : byRef.Assigned.Contains(site.Location.Range) ? site with { AssignedThroughCall = true }
                : site)],
        };
    }

    // what refers to each declaration, by its key: the expressions of the procedures that are bound to it, counted by what they do.
    private sealed class References
    {
        public DeclarationReferences Count { get; set; }

        public ImmutableArray<DeclarationReference>.Builder Sites { get; } = ImmutableArray.CreateBuilder<DeclarationReference>();
    }

    // code that refers to declarations: the body of a procedure, or the declarations of a module, which no procedure holds; with what the calls of its
    // module do with the arguments they take by reference, when that is known.
    private readonly record struct Code(SemanticId? Procedure, IEnumerable<ExpressionFact> Expressions, ByRefArguments? ByRef);

    private static IEnumerable<Code> CodeOf(IEnumerable<ProcedureSemanticModel> procedures, ModuleDeclarationFacts? declarations, ByRefArguments? byRef)
        => procedures.Select(procedure => new Code(procedure.Procedure, procedure.Expressions.Values, byRef))
            .Concat(declarations is null ? Enumerable.Empty<Code>() : [new Code(null, declarations.Expressions.Values, byRef)]);

    // a reference is a place in the code: the tree can hold an expression twice (the bound of an array of the module is also a child of its declaration),
    // and both are evaluated, as what the tree holds is.
    private static IEnumerable<ExpressionFact> Bound(IEnumerable<ExpressionFact> expressions)
        => expressions.Where(fact => fact.Binding is not null).DistinctBy(fact => (fact.Location, fact.Binding));

    // an argument that the call reads and does not write, though it is passed by reference, is read; one it is known to assign is assigned through it
    // (ByRefArguments).
    private static Dictionary<string, References> Tally(IEnumerable<Code> code)
    {
        var references = new Dictionary<string, References>(StringComparer.Ordinal);
        foreach (var (procedure, expressions, byRef) in code)
        {
            var bound = Bound(expressions)
                .GroupBy(fact => (Key: Key(fact.Binding!.Value), fact.Location.Range.Start))
                .SelectMany(group => group.Any(fact => fact.Flags.HasFlag(ValueExpressionSemanticFlags.ProcedureCall))
                    ? Call(procedure, [.. group])
                    : group.Select(fact => (Fact: fact, Kind: KindOf(fact, byRef), fact.Flags)))
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
                    AssignedThroughCall = kind is DeclarationReferenceKind.PassedAsArgument && byRef?.Assigned.Contains(fact.Location.Range) == true,
                });
            }
        }

        return references;
    }

    private static DeclarationReferenceKind KindOf(ExpressionFact fact, ByRefArguments? byRef) => fact.Flags switch
    {
        var flags when flags.HasFlag(ValueExpressionSemanticFlags.AssignmentTarget) => DeclarationReferenceKind.Write,
        var flags when flags.HasFlag(ValueExpressionSemanticFlags.PassedAsArgument) && byRef?.Read.Contains(fact.Location.Range) != true
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
