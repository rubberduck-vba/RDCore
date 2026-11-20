using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.JsonRpc;
using RDCore.Runtime.Support.Host;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Types.Complex;
using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Types.Abstract;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Shared;
using RDCore.SDK.Model.AST;
using RDCore.Runtime.Semantics;
using RDCore.Runtime.Execution;
using RDCore.Runtime.Semantics.Precompiler;
using RDCore.Runtime.LanguageCore.Symbols;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Workspace;
using System.Collections.Immutable;
using RDCore.SDK.Platform.Components;

namespace RDCore.Runtime.Support.Host.Handlers;

/// <summary>
/// Handles <c>rdcore/host/symbols/define</c>: reconstructs the runtime member symbols one module
/// declares from the language server's descriptors and defines them in the runtime session. Declared
/// type names are resolved against the intrinsic types and the standard library's own declared types; a
/// name that does not resolve stays <c>VBUnknownType</c> and is reported back.
/// </summary>
/// <remarks>
/// A module of a project the host does not run is defined in that project's analysis session (<see cref="IProjectAnalysisSessions"/>), which the request's
/// <see cref="DefineSymbolsParams.WorkspaceRoot"/> names; every other module is defined in the runtime session.
/// </remarks>
internal sealed class DefineSymbolsHandler(
    IEnvironmentSessionProvider sessionProvider,
    IProjectAnalysisSessions analyses,
    IErrorDetailsRecorder errorDetails,
    ILogger<DefineSymbolsHandler> logger) : RDCoreRequestHandler<DefineSymbolsParams, DefineSymbolsResult>
{
    protected override Task<DefineSymbolsResult> HandleAsync(DefineSymbolsParams request, CancellationToken token)
    {
        // a project that is analyzed and not run: no program runs in its session, and what is defined in it is defined at once.
        if (request.WorkspaceRoot is { } root && analyses.TryGet(root, out var analysis))
        {
            return Task.FromResult(Define(new Target(analysis.Session, analysis.Image, IsRun: false), request));
        }

        if (!sessionProvider.IsComposed)
        {
            logger.LogWarning(
                "rdcore/host/symbols/define received before the runtime session was composed; '{module}' was ignored.",
                request.ModuleName);
            return Task.FromResult(new DefineSymbolsResult());
        }

        // made against declarations a program in progress holds as they were: made again once the program is over, against them as they are declared - in
        // the order the requests came in, the latest of each module and phase.
        // only a request that carries the module's parse loads its code, and says what is wrong with it: one of symbols alone leaves a failure as it is.
        var session = sessionProvider.Session;
        var target = new Target(session, sessionProvider.Image, IsRun: true);
        var loadsCode = !request.DeclareTypesOnly && request.ParseResultJson.Length > 0;
        // a referenced project's module of a name is another module than the session's own of that name, and none the project gains.
        // what is wrong with its code refuses a run as a module of the project does, named after its project.
        var key = $"{request.Library}|{request.ModuleName}|{request.DeclareTypesOnly}|{request.CodeOnly}";
        var result = HeldDefinitions.Define(session, key, request.Library is null ? request.ModuleName : string.Empty,
            loadsCode ? defined => [.. defined.CodeErrors] : null, () => Define(target, request), () => [.. Define(target, request).CodeErrors],
            loadedAs: request.Library is { } library ? $"{library}.{request.ModuleName}" : null);

        // told once it is made: a statement typed at a stop from now on is given what the code as edited declares.
        ProgramEdits.Edited(session);
        return Task.FromResult(result);
    }

    // the session a module is defined in, and its code; only the runtime session's program runs, and reaches outside the platform.
    private sealed record class Target(IRuntimeSession Session, ProgramImage Image, bool IsRun);

    private DefineSymbolsResult Define(Target target, DefineSymbolsParams request)
    {
        var session = target.Session;
        if (request.Library is not null)
        {
            return DefineLibraryModule(target, request);
        }

        if (target.IsRun)
        {
            DefineClientModule(session, request);
        }

        ApplyDirectives(session, request);

        var unresolvedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var (defined, replaced, merged, skipped) = (0, 0, 0, new List<string>());
        if (!request.CodeOnly)
        {
            var isDead = DeadBranchesOf(target, request);
            // the three phases (DefineSymbolsParams.DeclareTypesOnly): the module's types are declared - its user-defined types and enumerations - so that
            // every declaration that names one is bound to a type that exists. Everything a request carries is read before anything of it is defined, which
            // is why the types are a request of their own: a request that binds declares its own types first, which the declare phase already did for a
            // workspace (first definition wins, and they are skipped), and which a module defined on its own needs.
            var types = request with { Symbols = [.. request.Symbols.Where(IsTypeDeclaration)] };
            var declaredTypes = DefineSymbols(session, types, [], replace: request.Replace, isDead);
            if (request.DeclareTypesOnly)
            {
                (defined, replaced, merged, skipped) = declaredTypes;
            }
            else
            {
                // then they are bound: a field of a type of another module, or of a type its own module declares after it, names a type that exists now.
                var boundTypes = DefineSymbols(session, types, unresolvedTypeNames, replace: true, isDead);
                var rest = request with { Symbols = [.. request.Symbols.Where(descriptor => !IsTypeDeclaration(descriptor))] };
                var bound = DefineSymbols(session, rest, unresolvedTypeNames, replace: request.Replace, isDead);
                // a type the declare phase defined is the one this request binds, not one it skipped.
                (defined, replaced, merged, skipped) = (declaredTypes.Defined + bound.Defined, boundTypes.Replaced + bound.Replaced,
                    declaredTypes.Merged + bound.Merged, bound.Skipped);

                // a class module's symbol is composed from a project without being read; what makes it the class its members declare, and the one that
                // implements the interfaces its directives name, is this, once its members are bound. A module that is not a class has nothing to compose.
                _ = request.ModuleUri is { } moduleUri
                    ? session.Symbols.TryComposeClassModule(moduleUri, request.ImplementedInterfaceNames, request.ImplementedInterfaceRanges)
                    : session.Symbols.TryComposeClassModule(request.ModuleName, request.ImplementedInterfaceNames, request.ImplementedInterfaceRanges);

                // MS-VBAL §5.2.4.1.2: a class module with VB_PredeclaredId = True has a default instance variable named after it, which the code of every
                // module names - so it is there before any code is loaded, which is after every module's symbols are.
                DefineDefaultInstance(session, request);
            }
        }

        // the module's procedures get their code now that everything they are keyed by is defined.
        var loadErrors = LoadCode(target, request);

        // a module that declares a function of a native library can call it as soon as it runs, and what makes the call is got ready now - in an environment that
        // lets a program make one at all.
        if (target.IsRun && session.Environment.AllowDllImports && !request.DeclareTypesOnly
            && request.Symbols.Any(symbol => symbol.Kind is SymbolDescriptorKind.ExternalProcedure or SymbolDescriptorKind.ExternalFunction))
        {
            sessionProvider.Outside.Libraries.Prepare();
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "📥 {module}: defined {defined} symbol(s), replaced {replaced}, merged {merged}, skipped {skipped}, {unresolved} unresolved type name(s).",
                request.ModuleName, defined, replaced, merged, skipped.Count, unresolvedTypeNames.Count);
        }

        return new DefineSymbolsResult
        {
            Defined = defined,
            Replaced = replaced,
            Skipped = skipped,
            UnresolvedTypeNames = [.. unresolvedTypeNames],
            CodeErrors = loadErrors,
            MergedDefinitions = merged,
        };
    }

    // what declares a type: a user-defined type, with its fields, and an enumeration, with its members (DefineSymbolsParams.DeclareTypesOnly).
    private static bool IsTypeDeclaration(SymbolDescriptor descriptor)
        => descriptor.Kind is SymbolDescriptorKind.UserDefinedType or SymbolDescriptorKind.Enum;

    // MS-VBAL §3.4: what the module's directives make dead, evaluated with the session's constants and the module's own - the request's directives, when it
    // sends them; null when it does not, and every declaration is defined as the first branch writes it.
    private Func<SourcePosition, bool>? DeadBranchesOf(Target target, DefineSymbolsParams request)
    {
        var session = target.Session;
        if (request.PrecompilerTriviaJson.Length == 0)
        {
            return null;
        }

        // a module is defined in phases, each request carrying the same directives: they are read and evaluated once per session and module, for as long as
        // the project's constants are what they were evaluated with.
        var evaluated = DeadRangesBySession.GetOrCreateValue(session);
        var key = request.ModuleUri?.ToString() ?? request.ModuleName;
        var constants = string.Join(";", (session.Symbols.ScopeOf(StaticSymbol.GlobalUri)?.Declarations ?? []).OfType<PrecompilerConstantSymbol>()
            .Select(constant => $"{constant.Name}={constant.Value.Handle.Value.BoxedValue}").Order(StringComparer.OrdinalIgnoreCase));
        if (!evaluated.TryGetValue(key, out var cached) || !string.Equals(cached.Json, request.PrecompilerTriviaJson, StringComparison.Ordinal)
            || !string.Equals(cached.Constants, constants, StringComparison.Ordinal))
        {
            var trivia = PlatformJson.Deserialize<ImmutableArray<RDCore.SDK.Model.AST.Abstract.SyntaxNode>>(request.PrecompilerTriviaJson);
            var ranges = trivia.IsDefaultOrEmpty
                ? []
                : PrecompilerLiveBranchEvaluator.GetDeadRanges(session, RuntimeExecutionPipeline.Create(session, target.Image, errorDetails).Expressions,
                    new RDCore.Runtime.Semantics.RuntimeEvaluationContext(StaticSymbol.GlobalUri), trivia);
            cached = (request.PrecompilerTriviaJson, constants, ranges);
            evaluated[key] = cached;
        }

        var dead = cached.Dead;
        return dead.IsEmpty ? null : position => dead.Any(range => position.CompareTo(range.Start) >= 0 && position.CompareTo(range.End) <= 0);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IRuntimeSession,
        System.Collections.Concurrent.ConcurrentDictionary<string, (string Json, string Constants, ImmutableArray<SourceRange> Dead)>> DeadRangesBySession = new();

    // Reads the module's symbols and defines them, the newest winning when asked to. The language server fuses a declaration written once per #If branch
    // into one symbol, each other branch's arriving beside it with the same identity (uri): of those the one of the live branch is defined, and a declaration
    // written in dead branches only is not, as MS-VBA compiles a module. Without the directives, the first that carries every site is, as it always was.
    // Property Get/Let/Set, and an Enum, a Type or an Event beside a namesake of the module, have identities of their own (Symbol.UriSuffix), so they stay
    // distinct.
    private static (int Defined, int Replaced, int Merged, List<string> Skipped) DefineSymbols(
        IRuntimeSession session, DefineSymbolsParams request, HashSet<string> unresolvedTypeNames, bool replace, Func<SourcePosition, bool>? isDead)
    {
        var (defined, replaced, merged, skipped) = (0, 0, 0, new List<string>());

        // sites of different kinds are not fused - a variable and a procedure of one name are a name declared twice, which is reported - but a Sub and a
        // Function are of one kind: the headers of one procedure written once per branch may be either.
        var notDefined = new HashSet<string>(StringComparer.Ordinal);
        var definedHere = new HashSet<string>(StringComparer.Ordinal);
        var resolveType = TypeResolver(session, request.ModuleUri, unresolvedTypeNames);
        foreach (var group in SymbolDescriptorReader.Read(request, resolveType).GroupBy(symbol => $"{symbol.Uri}|{KindOf(symbol)}"))
        {
            var sites = group.ToList();
            var uri = sites[0].Uri.ToString();

            // what a declaration that is not defined declares - the locals of a procedure whose every header is dead - is not defined either.
            if (notDefined.Contains(sites[0].ParentUri.ToString()))
            {
                notDefined.Add(uri);
                continue;
            }

            merged += sites.Count - 1;
            var symbol = sites.Find(candidate => candidate is WorkspaceSymbol { Definitions.IsDefaultOrEmpty: false }) ?? sites[0];
            if (isDead is not null && symbol is WorkspaceSymbol declared)
            {
                if (LiveDeclarationOf(declared, sites, isDead) is not { } live)
                {
                    notDefined.Add(uri);
                    continue;
                }

                symbol = live;
            }
            else if (symbol is VBUserDefinedTypeMemberSymbol type)
            {
                // a field written in each branch is the first branch's.
                symbol = type with { Members = [.. type.Members.GroupBy(field => field.Uri.ToString()).Select(fields => fields.First())] };
            }

            symbol = WithLiveLocals(symbol, isDead);

            // a live declaration of a name this request has defined already, of another kind, is that name declared twice: the first stands.
            if (!definedHere.Add(uri))
            {
                skipped.Add(symbol.Name);
                continue;
            }

            if (session.Symbols.TryDefine(symbol, symbol.ScopeKind))
            {
                defined++;
            }
            else if (replace && session.Symbols.TryRedefine(symbol, symbol.ScopeKind))
            {
                // the caller says this module has been re-read, so the newest definition wins: the
                // previous one may have had different locals, a different declared type, or a body
                // this one no longer has. A variable declared as it was keeps what it holds - a shell
                // re-reads the whole module for every line, and a value that did not survive that would
                // not survive to the next line.
                replaced++;
            }
            else
            {
                skipped.Add(symbol.Name);
            }
        }

        return (defined, replaced, merged, skipped);
    }

    private static Func<string, VBType?> TypeResolver(IRuntimeSession session, Uri? moduleUri, HashSet<string> unresolvedTypeNames) => typeName =>
    {
        if (IntrinsicVBTypes.TryResolve(typeName, session.Environment.Is64Bit, out var type))
        {
            return type;
        }

        // the standard library's own declared types are in this session too — the host injects them
        // into every project whether or not a .rdproj mentions the library (RD-VBAL §6.1) — so
        // `As VbDayOfWeek` and `As ErrObject` bind here instead of being reported unresolved. A
        // workspace type may still not be defined yet, since this defines one module at a time, and
        // stays unresolved until the module is read again.
        // resolved as seen from the module being defined, not from the global scope: a standard
        // module's members reach the project scope, and the project scope is only an ancestor of a
        // module's own. A request that names no module has no such vantage point, and resolves
        // intrinsics only.
        if (moduleUri is not null
            && DeclaredTypes.Resolve(session.Symbols.Resolver, typeName, moduleUri, session.Environment.Is64Bit,
                ProjectVisibility.Instance, ProjectVisibility.ProjectOf(session.Symbols, moduleUri)) is { } declared)
        {
            return declared;
        }

        unresolvedTypeNames.Add(typeName);
        return null;
    };

    // a module of a project the session's project references (DefineSymbolsParams.Library): every name it declares, under that project's root, beside the
    // session's own modules; a module of the session's own project with the same name keeps the name. Nothing is done by the module's name. Its types are
    // declared, then bound, as a module of the session's own project's are, and its code is loaded once every module is defined: the program calls into it.
    // Of a name declared in several #If branches, the first site that carries them all is defined: the session's constants are not the referenced project's.
    private DefineSymbolsResult DefineLibraryModule(Target target, DefineSymbolsParams request)
    {
        var session = target.Session;
        if (request.WorkspaceRoot is not { } workspaceRoot || SymbolDescriptorReader.LibraryModuleOf(request) is not { } module)
        {
            return new DefineSymbolsResult();
        }

        if (request.CodeOnly)
        {
            return new DefineSymbolsResult { CodeErrors = LoadLibraryCode(target, request, module.Uri) };
        }

        var unresolvedTypeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolveType = TypeResolver(session, module.Uri, unresolvedTypeNames);
        var types = request with { Symbols = [.. request.Symbols.Where(IsTypeDeclaration)] };
        var rest = request with { Symbols = [.. request.Symbols.Where(descriptor => !IsTypeDeclaration(descriptor))] };
        var (defined, replaced, merged, skipped) = (0, 0, 0, new List<string>());
        DefineSymbolsParams[] phases = request.DeclareTypesOnly ? [types] : [types, rest];
        foreach (var phase in phases)
        {
            var sites = SymbolDescriptorReader.Read(phase, resolveType).GroupBy(symbol => $"{symbol.Uri}|{KindOf(symbol)}").ToList();
            merged += sites.Sum(group => group.Count() - 1);
            var members = sites.Select(group
                => group.FirstOrDefault(site => site is WorkspaceSymbol { Definitions.IsDefaultOrEmpty: false }) ?? group.First());

            // the project's symbol is defined with its first module, and stays; the module is defined again with its directives as they are sent. A
            // type is bound once every module's types are declared, which is why the request that binds defines it again. A class's default instance is
            // defined once and stays, as the session's own are (DefineDefaultInstance): it keeps the object it holds.
            var bindsTypes = !request.DeclareTypesOnly && ReferenceEquals(phase, types);
            foreach (var name in ReferencedProjectSymbols.Defined(workspaceRoot, request.Library!, [module, .. members], request.LibraryPriority))
            {
                if (session.Symbols.TryDefine(name, name.ScopeKind))
                {
                    defined++;
                }
                else if (name is VBProjectSymbol or VBPredeclaredInstanceSymbol)
                {
                    continue;
                }
                else if ((request.Replace || bindsTypes || name is VBModuleSymbol) && session.Symbols.TryRedefine(name, name.ScopeKind))
                {
                    replaced++;
                }
                else
                {
                    skipped.Add(name.Name);
                }
            }
        }

        // MS-VBAL §5.2.4.2: a class is composed from what is defined under it once its members are bound, as one of the session's own project is, so that
        // the members of an object of it, and of its default instance, are bound from the session's code.
        if (!request.DeclareTypesOnly && module is VBClassModuleSymbol)
        {
            _ = session.Symbols.TryComposeClassModule(module.Uri, request.ImplementedInterfaceNames, request.ImplementedInterfaceRanges);
        }

        // a class that is no longer predeclared has no default instance in the session.
        if (module is VBClassModuleSymbol classModule && !classModule.GetProperty(SymbolProperties.PredeclaredId))
        {
            var instance = ReferencedProjectSymbols.DefaultInstanceOf((VBClassModuleSymbol)classModule.With(SymbolProperties.Library, request.Library!));
            _ = session.Symbols.TryUndefine(instance, instance.ScopeKind);
        }

        return new DefineSymbolsResult
        {
            Defined = defined,
            Replaced = replaced,
            Skipped = skipped,
            UnresolvedTypeNames = [.. unresolvedTypeNames],
            MergedDefinitions = merged,
        };
    }

    // what fuses with what: a procedure's headers whatever their kind, and otherwise a symbol of the same type.
    private static string KindOf(Symbol symbol) => symbol is VBProcedureMemberSymbol or VBFunctionMemberSymbol ? "procedure" : symbol.GetType().Name;

    // MS-VBAL §3.4, §5.6.10: the locals of a procedure that the live code declares. A Dim, a Static or a Const of a dead branch is not compiled, and declares
    // nothing; what a name declares that the module or the procedure declares only in #If branches is declared where each of those is dead, and not
    // otherwise (SymbolProperties.UnlessLive). Without the directives, every branch is read as live.
    private static Symbol WithLiveLocals(Symbol symbol, Func<SourcePosition, bool>? isDead)
    {
        static bool IsConditional(BoundTypedSymbol local) => !local.GetProperty(SymbolProperties.UnlessLive).IsDefaultOrEmpty;
        static bool IsDeclaredByAStatement(BoundTypedSymbol local) => local is VBLocalVariableSymbol { DeclaredBy: LocalDeclarationKind.Dim } or VBLocalConstantSymbol;
        bool IsDeclared(BoundTypedSymbol local) => IsConditional(local)
            ? isDead is not null && local.GetProperty(SymbolProperties.UnlessLive).All(isDead)
            : isDead is null || !IsDeclaredByAStatement(local) || !isDead(local.Range.Start);

        // what is declared is then declared whatever the branches are: the scope of the procedure has it. A name declared in more than one branch arrives
        // once for each, and is declared once: by the first of them that is declared.
        ImmutableArray<BoundTypedSymbol> Live(ImmutableArray<BoundTypedSymbol> locals)
            => [.. locals.Where(IsDeclared).DistinctBy(local => local.Name, StringComparer.OrdinalIgnoreCase)
                .Select(local => IsConditional(local) ? (BoundTypedSymbol)local.With(SymbolProperties.UnlessLive, []) : local)];

        bool IsAsDeclared(ImmutableArray<BoundTypedSymbol> locals)
            => locals.All(local => !IsConditional(local) && IsDeclared(local)) && locals.DistinctBy(local => local.Name, StringComparer.OrdinalIgnoreCase).Count() == locals.Length;

        return symbol switch
        {
            VBProcedureMemberSymbol procedure when !IsAsDeclared(procedure.Locals) => procedure with { Locals = Live(procedure.Locals) },
            VBReturningMemberSymbol returning when !IsAsDeclared(returning.Locals) => returning with { Locals = Live(returning.Locals) },
            _ => symbol,
        };
    }

    // the declaration of the live branch among the sites of one identity (WorkspaceSymbol.LiveDeclaration) - and of a Type, the fields of the live branches.
    private static Symbol? LiveDeclarationOf(WorkspaceSymbol primary, List<Symbol> sites, Func<SourcePosition, bool> isDead)
    {
        var live = (primary with { Alternatives = [.. sites.Where(site => !ReferenceEquals(site, primary)).OfType<WorkspaceSymbol>()] }).LiveDeclaration(isDead);
        return live is VBUserDefinedTypeMemberSymbol type
            ? type with
            {
                Members = [.. type.Members.GroupBy(field => field.Uri.ToString()).Select(fields => fields.FirstOrDefault(field => !isDead(field.Range.Start))).OfType<VBTypeMemberSymbol>()],
            }
            : live;
    }

    private ImmutableArray<string> LoadCode(Target target, DefineSymbolsParams request)
    {
        var session = target.Session;
        if (request.ParseResultJson.Length == 0
            || PlatformJson.Deserialize<ModuleParseResult>(request.ParseResultJson) is not { } parseResult
            || !session.Symbols.TryGetModule(request.ModuleName, out var module))
        {
            return [];
        }

        // a UserControl's, a PropertyPage's or a UserDocument's code names the members of the class its designer block names (VBClassModuleSymbol.BaseClass):
        // the project's symbol of the module is made without its source being read, and is given its base class before the code is bound.
        if (module is VBClassModuleSymbol classModule && !Equals(parseResult.Designer?.BaseClass, classModule.BaseClass)
            && session.Symbols.TryRedefine(classModule with { BaseClass = parseResult.Designer?.BaseClass }, classModule.ScopeKind)
            && session.Symbols.TryGetModule(request.ModuleName, out var based))
        {
            module = based;
        }

        return Load(target, request.ModuleName, module, parseResult);
    }

    // a referenced project's module is found by its address: the session's own module of its name is another module.
    private ImmutableArray<string> LoadLibraryCode(Target target, DefineSymbolsParams request, Uri moduleUri)
    {
        if (request.ParseResultJson.Length == 0
            || PlatformJson.Deserialize<ModuleParseResult>(request.ParseResultJson) is not { } parseResult
            || !target.Session.Symbols.TryGetModule(moduleUri, out var module))
        {
            return [];
        }

        return Load(target, $"{request.Library}.{request.ModuleName}", module, parseResult);
    }

    private ImmutableArray<string> Load(Target target, string name, VBModuleSymbol module, ModuleParseResult parseResult)
    {
        var session = target.Session;
        var errors = new ModuleLoader(target.Session, target.Image, errorDetails).Load(module, parseResult);

        // a module is loaded again at every edit of it: what a load failed with is said once, as long as the next fails with the same, and so is that it loads again.
        switch (LoadFailuresBySession.GetOrCreateValue(session).Record(name, errors))
        {
            case ModuleLoadChange.Failed:
                foreach (var error in errors)
                {
                    logger.LogWarning("{module} was not loaded: {error}", name, error);
                }
                break;

            case ModuleLoadChange.Recovered:
                logger.LogInformation("{module} loads again.", name);
                break;
        }

        return errors;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IRuntimeSession, ModuleLoadFailures> LoadFailuresBySession = new();

    // the client's own module, which the project does not list - a shell's program, in a workspace of its own or in one whose project file could not be read -
    // is a standard module of the session, as the language server binds it (SymbolSyncService.SyncModuleAsync): its members are then a module's, and it runs.
    // The project is not changed, and a module of any other name the session does not have is still not found. Nor is one named after the project or a library
    // it references, which the language server refuses to run: the project, the projects it references and its procedural modules are matched in one tier
    // (MS-VBAL §5.6.10), so the name would match twice, and every name qualified by it would be invalid.
    private static void DefineClientModule(IRuntimeSession session, DefineSymbolsParams request)
    {
        if (!request.SessionModule || request.CodeOnly || request.WorkspaceRoot is not { } workspaceRoot || request.ModuleName.Length == 0
            || session.Symbols.TryGetModule(request.ModuleName, out _)
            || string.Equals(request.ModuleName, session.Errors.ProjectName, StringComparison.OrdinalIgnoreCase)
            || session.References.Any(reference => string.Equals(reference.Name, request.ModuleName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var module = new VBStandardModuleSymbol(workspaceRoot, workspaceRoot, request.ModuleName);
        _ = session.Symbols.TryDefine(module, module.ScopeKind);
    }

    // MS-VBAL 5.2.1: the module symbol is composed from the .rdproj without parsing anything, so its
    // Option directives are not known until the language server sends them here. They are what an
    // activation of one of the module's procedures runs under - Option Compare decides how its
    // relational operators compare Strings, Option Base what `Dim a(10)` means - and both were read off
    // a frame that was never given any, so both silently took their default. A class module's are applied
    // as a standard module's are; otherwise its code is checked and runs as if it stated none of them,
    // Option Explicit included.
    private static void ApplyDirectives(IRuntimeSession session, DefineSymbolsParams request)
    {
        if (!session.Symbols.TryGetModule(request.ModuleName, out var module)
            || module.Directives == request.Directives && !(module is VBClassModuleSymbol && AttributesDiffer(module, request.Directives)))
        {
            return;
        }

        // the same undefine/define the Replace path uses: a module symbol allocates no storage of its
        // own, and its members are keyed by their own Uris rather than held by it.
        // A class module's attributes are its properties as well, as the language server's module symbols have them (WorkspaceSymbolResolver.Compose).
        Symbol updated = module with { Directives = request.Directives };
        if (updated is VBClassModuleSymbol)
        {
            updated = updated
                .With(SymbolProperties.PredeclaredId, request.Directives.PredeclaredId)
                .With(SymbolProperties.Creatable, request.Directives.Creatable)
                .With(SymbolProperties.Extensible, request.Directives.Extensible);
        }

        if (session.Symbols.TryUndefine(module, module.ScopeKind))
        {
            session.Symbols.TryDefine(updated, updated.ScopeKind);
        }
    }

    // a class module composed from a project has none of its attributes as properties until it is first defined: VB_Creatable's default is True.
    private static bool AttributesDiffer(Symbol module, ModuleDirectives directives)
        => module.GetProperty(SymbolProperties.PredeclaredId) != directives.PredeclaredId
            || module.GetProperty(SymbolProperties.Creatable) != directives.Creatable
            || module.GetProperty(SymbolProperties.Extensible) != directives.Extensible;

    // the default instance of a predeclared class, defined once: a module defined again - a shell reads its module again for every line - keeps the object
    // its default instance holds, which auto-instantiation makes of the class as it is when it is referred to. A class that is no longer predeclared has none.
    private static void DefineDefaultInstance(IRuntimeSession session, DefineSymbolsParams request)
    {
        if (!session.Symbols.TryGetModule(request.ModuleName, out var module) || module is not VBClassModuleSymbol classModule)
        {
            return;
        }

        var instance = new VBPredeclaredInstanceSymbol(classModule);
        if (request.Directives.PredeclaredId)
        {
            _ = session.Symbols.TryDefine(instance, instance.ScopeKind);
        }
        else
        {
            _ = session.Symbols.TryUndefine(instance, instance.ScopeKind);
        }
    }
}
