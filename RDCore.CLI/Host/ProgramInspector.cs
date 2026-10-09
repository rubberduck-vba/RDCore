using RDCore.Runtime.Execution.Debugging;
using RDCore.Runtime.Execution.Frames;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Source;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Bindings;
using RDCore.SDK.Platform.Protocol;
using RDCore.SDK.Runtime.Abstract.Execution;

namespace RDCore.CLI.Host;

/// <summary>
/// What a program that waits looks like from outside: its activations, and their variables.
/// </summary>
/// <remarks>
/// The activations stay on the session's call stack with their locals while the program waits, so reading them is reading the stack. A variable that has parts - an array,
/// a user-defined type - is given a reference, which stands for its parts for as long as the program waits: a client asks for them when it wants them, and what is
/// not asked for is not made. The references are forgotten when the program goes on, because what they stand for is then not what it was.
/// </remarks>
/// <param name="provider">The provider of the session.</param>
internal sealed class ProgramInspector(IEnvironmentSessionProvider provider)
{
    private readonly Dictionary<int, IReadOnlyList<DebugPart>> _parts = [];
    private int _next;

    /// <summary>
    /// Forgets the references: the program goes on, or is over.
    /// </summary>
    public void Forget()
    {
        _parts.Clear();
        _next = 0;
    }

    /// <summary>
    /// The activations of the program, innermost first.
    /// </summary>
    public HostDebugStackResult Stack()
        => new()
        {
            Frames =
            [
                .. provider.Session.CallStack.Frames.OfType<CallStackFrame>().Select((frame, index) =>
                {
                    var location = frame.Body is { } body && frame.Pc >= 0 && frame.Pc < body.Items.Length ? body.Items[frame.Pc].Node?.SourceLocation : null;
                    return new HostStackFrame(
                        index,
                        frame.Procedure?.Name ?? frame.StaticSymbol.Name,
                        ModuleNameOf(frame),
                        location?.Range.Start.Line ?? -1,
                        location?.Range.Start.Character ?? -1,
                        ReturnLines(frame),
                        HandlerOf(frame));
                }),
            ],
        };

    // an activation is in a handler from the error it caught until the Resume that ends it: the label the handler begins at.
    private static string? HandlerOf(CallStackFrame frame)
        => frame.ErrorHandler is { ActiveError: not null, HandlerEntry: { } entry } && frame.Body is { } body ? body.LabelAt(entry) : null;

    // the GoSub statements the activation has not returned from, innermost first: each return offset is the instruction after its GoSub.
    private static int[] ReturnLines(CallStackFrame frame)
        => frame.Body is not { } body
            ? []
            : [.. frame.GoSubReturns
                .Select(returnOffset => returnOffset - 1 >= 0 && returnOffset - 1 < body.Items.Length ? body.Items[returnOffset - 1].Node?.SourceLocation.Range.Start.Line ?? -1 : -1)];

    /// <summary>
    /// The variables of an activation, or the parts of one of them.
    /// </summary>
    /// <param name="frameId">The activation, by its place on the stack.</param>
    /// <param name="scope">Which of its variables.</param>
    /// <param name="reference">The reference of a variable that has parts, or <c>0</c>.</param>
    public HostDebugVariablesResult Variables(int frameId, HostVariableScope scope, int reference)
    {
        if (reference > 0)
        {
            return _parts.TryGetValue(reference, out var parts)
                ? new HostDebugVariablesResult
                {
                    Variables = [.. parts.Select(part => Describe(part.Name, part.Value))],
                    NotListed = 0,
                }
                : new HostDebugVariablesResult();
        }

        var frames = provider.Session.CallStack.Frames.OfType<CallStackFrame>().ToArray();
        if (frameId < 0 || frameId >= frames.Length)
        {
            return new HostDebugVariablesResult();
        }

        var frame = frames[frameId];
        return new HostDebugVariablesResult { Variables = [.. (scope is HostVariableScope.Locals ? Locals(frame) : Module(frame))] };
    }

    // the parameters, then the variables the procedure declares - an implicit one is declared by the first use - and the value it returns, which is a variable
    // of its own named like the procedure (MS-VBAL 5.3.1).
    private IEnumerable<HostVariable> Locals(CallStackFrame frame)
    {
        var (parameters, locals) = frame.Procedure switch
        {
            VBFunctionMemberSymbol function => (function.Parameters.Cast<Symbol>(), function.Locals.Cast<Symbol>()),
            VBProcedureMemberSymbol procedure => (procedure.Parameters.Cast<Symbol>(), procedure.Locals.Cast<Symbol>()),
            _ => ([], Enumerable.Empty<Symbol>()),
        };

        foreach (var symbol in InDeclarationOrder(parameters.Concat(locals).Where(symbol => symbol is VBParameterSymbol or VBLocalVariableSymbol)))
        {
            yield return Describe(symbol.Name, ValueOf(frame, symbol));
        }

        if (frame.Procedure is VBFunctionMemberSymbol returning && frame.ReturnValue is { } result)
        {
            yield return Describe(returning.Name, result);
        }
    }

    private IEnumerable<HostVariable> Module(CallStackFrame frame)
    {
        if (frame.Procedure is not { } procedure)
        {
            yield break;
        }

        foreach (var field in InDeclarationOrder(provider.Session.Symbols.MembersOf(procedure.ParentUri).OfType<VBModuleFieldVariableMemberSymbol>()))
        {
            yield return Describe(field.Name, ValueOf(frame, field));
        }
    }

    // the order the source declares them in, which is not the order a symbol table keeps them in. An implicit declaration is where the name is first used.
    private static IEnumerable<TSymbol> InDeclarationOrder<TSymbol>(IEnumerable<TSymbol> symbols) where TSymbol : Symbol
        => symbols.OrderBy(symbol => RangeOf(symbol).Start.Line).ThenBy(symbol => RangeOf(symbol).Start.Character);

    private static SourceRange RangeOf(Symbol symbol) => symbol switch
    {
        BoundTypedSymbol bound => bound.Range,
        WorkspaceSymbol workspace => workspace.Range,
        _ => default,
    };

    // a variable that nothing has allocated yet has no value to show.
    private VBTypedValue? ValueOf(CallStackFrame frame, Symbol symbol)
    {
        if (symbol is not ITypedSymbol typed)
        {
            return null;
        }

        try
        {
            IBindingHandle? handle = null;
            if (!frame.TryResolve(symbol, out handle))
            {
                handle = provider.Session.Symbols.Resolver.GetValue(symbol);
            }

            return typed.ResolvedType.CreateValue(handle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException or InvalidCastException)
        {
            return null;
        }
    }

    internal HostVariable Describe(string name, VBTypedValue? value)
    {
        if (value is null)
        {
            return new HostVariable(name, Resources.Host_ValueUnavailable, string.Empty);
        }

        var view = DebugValueFormatter.Describe(value);
        if (view.Parts is not { Count: > 0 } parts)
        {
            return new HostVariable(name, view.Display, view.Type);
        }

        var reference = ++_next;
        _parts[reference] = parts;
        return new HostVariable(name, view.Display, view.Type, reference);
    }

    // the module is the fragment of the procedure's parent: "Program" for Program.Main.
    private static string ModuleNameOf(CallStackFrame frame)
        => frame.Procedure?.ParentUri.Fragment.TrimStart('#') is { Length: > 0 } module ? module : string.Empty;
}
