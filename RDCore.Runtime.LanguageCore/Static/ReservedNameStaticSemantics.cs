using RDCore.SDK.Model.AST.Abstract;
using RDCore.SDK.Model.AST.Declarations;
using RDCore.SDK.Model.AST.Expressions;
using RDCore.SDK.Model.Errors;
using System.Collections.Immutable;
using RDCore.SDK;
using RDCore.Runtime.LanguageCore.Errors;

namespace RDCore.Runtime.LanguageCore.Static;

// MS-VBAL §3.3.5.2
// MS-VBAL §3.3.5.2 is silent about a reserved name written without its arguments: MS-VBA refuses it as a syntax error, not Argument not optional.
public static class ReservedNameStaticSemantics
{
    // how many arguments each takes, which is the one way MS-VBA parses a call of it.
    private static readonly ImmutableDictionary<string, int> _arities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["Abs"] = 1, ["CBool"] = 1, ["CByte"] = 1, ["CCur"] = 1, ["CDate"] = 1, ["CDbl"] = 1, ["CInt"] = 1, ["CLng"] = 1, ["CLngLng"] = 1, ["CLngPtr"] = 1,
        ["CSng"] = 1, ["CStr"] = 1, ["CVar"] = 1, ["CVErr"] = 1, ["Fix"] = 1, ["Int"] = 1, ["Len"] = 1, ["LenB"] = 1, ["Sgn"] = 1, ["String"] = 2,
    }.ToImmutableDictionary(StringComparer.OrdinalIgnoreCase);

    public static bool IsReservedName(string name) => _arities.ContainsKey(name);

    public static VBCompileErrorInfo? CallError(IndexExpressionNode call)
    {
        if (call.Callee is not SimpleNameExpressionNode { IdentifierName: var name } || !_arities.TryGetValue(name, out var arity)
            || call.Arguments.Any(argument => argument is NamedArgumentNode))
        {
            return null;
        }

        var given = call.Arguments.Count(argument => argument is not MissingArgumentNode);
        return given == arity && given == call.Arguments.Length
            ? null
            : VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, call.Location, string.Format(Exceptions.VBCompileError_ReservedNameArguments_Verbose, name, arity));
    }

    public static ImmutableArray<VBCompileErrorInfo> CheckSyntax(ModuleNode module)
    {
        var nodes = module.Descendants().ToList();
        // a callee is written with its arguments, and checked with them; a member of what qualifies it (`VBA.LenB`) is one like any other.
        var callees = nodes.OfType<IndexExpressionNode>().Select(call => (object)call.Callee)
            .Concat(nodes.OfType<MemberAccessExpressionNode>().Select(access => (object)access.Member))
            .ToHashSet(ReferenceEqualityComparer.Instance);
        return
        [
            .. nodes.OfType<SimpleNameExpressionNode>()
                .Where(name => _arities.TryGetValue(name.IdentifierName, out _) && !callees.Contains(name))
                .Select(name => VBCompileErrorInfo.For(VBCompileErrorId.SyntaxError, name.Location,
                    string.Format(Exceptions.VBCompileError_ReservedNameArguments_Verbose, name.IdentifierName, _arities[name.IdentifierName]))),
        ];
    }
}
