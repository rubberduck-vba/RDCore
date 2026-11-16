using RDCore.SDK.Runtime.Abstract.Execution;
using System.Runtime.CompilerServices;

namespace RDCore.Runtime.StdLib;

/// <summary>
/// The classes of the standard library whose objects hold something - a <c>Collection</c> its members, an enumerator its place, a <c>RegExp</c> its pattern,
/// a match collection its matches, a match what its groups matched - as a session has them: one of each, which every dispatcher composed on the session
/// reaches (<see cref="StdLibDispatcher.For"/>), whichever run or statement it was composed for. An object made while one program ran is read with what it
/// holds by a statement run while that program waits, and what it holds is let go of once, when it is destroyed (<see cref="ISessionObjects.OnDestroying"/>),
/// whichever run destroys it.
/// </summary>
internal sealed class SessionLibraryObjects
{
    private static readonly ConditionalWeakTable<IRuntimeSession, SessionLibraryObjects> Sessions = [];

    private SessionLibraryObjects(IRuntimeSession session)
    {
        // a collection makes the enumerator its For Each is driven by, which is an object of a class of its own.
        Enumerators = new StdEnumVariant(session);

        // a RegExp makes the collection of its matches, which makes each match, which makes what its groups matched.
        SubMatches = new StdSubMatches(session, Enumerators);
        Match = new StdMatch(session, SubMatches);
        Matches = new StdMatchCollection(session, Match, Enumerators);
        RegExps = new StdRegExp(session, Matches);
        Collections = new StdCollection(session, Enumerators);
    }

    /// <summary>The objects of the standard library's classes in <paramref name="session"/>.</summary>
    public static SessionLibraryObjects Of(IRuntimeSession session)
    {
        // made once for the session: each class is told of the objects the session destroys from then on, and a second one would be told too.
        lock (Sessions)
        {
            return Sessions.GetValue(session, created => new SessionLibraryObjects(created));
        }
    }

    /// <summary>The enumerators a <c>For Each</c> over a collection of the library is driven by.</summary>
    public StdEnumVariant Enumerators { get; }

    /// <summary>What the groups of each match matched.</summary>
    public StdSubMatches SubMatches { get; }

    /// <summary>The matches of a regular expression.</summary>
    public StdMatch Match { get; }

    /// <summary>The collections of the matches of a regular expression.</summary>
    public StdMatchCollection Matches { get; }

    /// <summary>The regular expressions.</summary>
    public StdRegExp RegExps { get; }

    /// <summary>The <c>Collection</c> objects.</summary>
    public StdCollection Collections { get; }
}
