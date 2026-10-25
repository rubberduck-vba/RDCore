using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Symbols;
using RDCore.SDK.Model.Symbols.Abstract;
using RDCore.SDK.Model.Symbols.VBProject;
using RDCore.SDK.Model.Values.Intrinsic;
using RDCore.SDK.Runtime.Abstract.Execution;
using RDCore.SDK.Runtime.Abstract.StdLib;
using RDCore.SDK.Runtime.StdLib;
using RDCore.SDK.Semantics.Static.Abstract;

namespace RDCore.SDK.Semantics.Static;

/// <summary>
/// Finds the class of the object a <c>CreateObject</c> call creates, from the programmatic identifier it is written with (<see cref="ObjectCreationFact"/>).
/// </summary>
/// <remarks>
/// It looks where the runtime does - a class the platform implements itself, then the classes of the libraries - and finds out no more than the code says: the
/// identifier is a literal, or a constant whose value is one, and the object is created on this machine. What the class is at run time is the server's to say.
/// </remarks>
internal static class ObjectCreationFacts
{
    private static readonly string CreateObjectTarget = StdLibSymbolReader.ExternalTargetOf(
        typeof(IStdInteractionModule), typeof(IStdInteractionModule).GetMethod(nameof(IStdInteractionModule.CreateObject))!);

    /// <summary>
    /// The class of the object <paramref name="call"/> creates, or <see langword="null"/> when it is not a call of <c>CreateObject</c>, or its class is not
    /// written as a constant, or nothing describes it.
    /// </summary>
    /// <param name="context">The context the call was evaluated in.</param>
    /// <param name="call">The call.</param>
    /// <param name="procedure">The procedure it calls.</param>
    public static ObjectCreationFact? Of(StaticEvaluationContext context, IndexExpressionNode call, Symbol procedure)
    {
        if (procedure.GetProperty(SymbolProperties.ExternalTarget) != CreateObjectTarget || procedure is not VBReturningMemberSymbol { Parameters: [var objectClass, var serverName] })
        {
            return null;
        }

        // an object created on another machine is of the class that machine has.
        if (TextOf(context, ArgumentFor(call, 0, objectClass.Name)) is not { Length: > 0 } progId
            || ArgumentFor(call, 1, serverName.Name) is { } server && TextOf(context, server) is not { Length: 0 })
        {
            return null;
        }

        var parts = progId.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        // a class of the platform's own, by the last part of the identifier, as CreateObject finds it.
        if (ClassOf(context, StdLibSymbolProvider.LibraryName, parts[^1], progId) is { } native)
        {
            return new(progId, StdLibSymbolProvider.LibraryName, native.Name, ObjectClassSource.StandardLibrary);
        }

        // a class of a referenced library is commonly named by the identifier's second part (`Scripting.Dictionary`, `DAO.DBEngine.120`).
        if (ClassOf(context, parts[0], parts[1], progId) is { } referenced)
        {
            return new(progId, parts[0], referenced.Name, ObjectClassSource.ReferencedLibrary);
        }

        // otherwise the library's description says which of its classes it is.
        if (context.Environment?.Libraries is not { } libraries || !libraries.TryGet(parts[0], out var library)
            || library.Classes.FirstOrDefault(described => described.ProgId is { } describedId && Matches(describedId, progId)) is not { } found)
        {
            return null;
        }

        return ClassOf(context, library.Name, found.Name, progId) is { } declared
            ? new(progId, library.Name, declared.Name, ObjectClassSource.ReferencedLibrary)
            : new(progId, library.Name, found.Name, ObjectClassSource.UnreferencedLibrary);
    }

    // the class of the name in the library, as the project resolves it, when it is the one the identifier names.
    private static VBClassModuleSymbol? ClassOf(StaticEvaluationContext context, string library, string name, string progId)
        => VBProjectSymbol.ResolveQualifiedType(context.Resolver, library, name, context.Scope.Uri).Symbol is VBClassModuleSymbol classModule
            && classModule.GetProperty(SymbolProperties.ProgId) is { Length: > 0 } describedId && Matches(describedId, progId)
                ? classModule
                : null;

    // the same identifier, without regard to case; or one that names no version, of the class whose identifier names one (`DAO.DBEngine`, `DAO.DBEngine.120`).
    private static bool Matches(string described, string written)
        => string.Equals(described, written, StringComparison.OrdinalIgnoreCase)
        || described.Length > written.Length + 1 && described.StartsWith(written, StringComparison.OrdinalIgnoreCase) && described[written.Length] == '.'
            && described[(written.Length + 1)..].All(character => char.IsAsciiDigit(character) || character == '.');

    // the argument of a parameter, given by its position or by its name.
    private static ExpressionNode? ArgumentFor(IndexExpressionNode call, int position, string name)
        => call.Arguments.OfType<NamedArgumentNode>().FirstOrDefault(named => string.Equals(named.Name, name, StringComparison.OrdinalIgnoreCase))?.Value
        ?? (position < call.Arguments.Length && call.Arguments[position] is not (NamedArgumentNode or MissingArgumentNode) ? call.Arguments[position] : null);

    // a string literal, or the name of a constant whose value is one.
    private static string? TextOf(StaticEvaluationContext context, ExpressionNode? argument) => argument switch
    {
        LiteralExpressionNode { StaticValue: VBStringValue text } => text.Value,
        SimpleNameExpressionNode name => context.Resolver.ResolveValue(name, ScopeKind.Local, context.Scope.Uri).Symbol switch
        {
            VBConstantMemberSymbol { Value: LiteralExpressionNode { StaticValue: VBStringValue text } } => text.Value,
            VBLocalConstantSymbol { Value: LiteralExpressionNode { StaticValue: VBStringValue text } } => text.Value,
            _ => null,
        },
        _ => null,
    };
}
