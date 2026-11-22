using RDCore.Runtime.Execution;

namespace RDCore.Runtime.Compiled;

// the temporaries a call's arguments and its target made, put in the order MS-VBA lets go of them once they are evaluated, as the interpreter puts them
// (RuntimeExpressionEvaluator.ArgumentCalls, ObjectReferences.SettleCall): what the call is given itself last, in the order of the parameters, then the
// target's, after what the calls inside them were given. A call is not made inside its own arguments in one activation, so each call the body makes has
// one settlement, kept for the next time it is made.
public sealed partial class CompiledActivation
{
    private CallSettlement?[]? _settlements;

    /// <summary>
    /// Begins the settlement of the call <paramref name="callee"/> of the body, before its arguments are evaluated.
    /// </summary>
    /// <param name="callee">The number of the call among the body's callees.</param>
    public void BeginSettling(int callee)
    {
        var settlements = _settlements ??= new CallSettlement?[_body.Callees.Count];
        (settlements[callee] ??= new CallSettlement()).Begin(_session.Objects.TemporaryCount);
    }

    /// <summary>
    /// Ends the piece open, if any, and marks the temporaries made from now on as those of the argument given to the parameter at
    /// <paramref name="owner"/>, or of the target (<see cref="int.MaxValue"/>).
    /// </summary>
    /// <param name="callee">The number of the call among the body's callees.</param>
    /// <param name="owner">The index of the parameter, or <see cref="int.MaxValue"/> for the target.</param>
    /// <param name="gives">
    /// Whether the piece is a call that returns an object, which the call is given itself: the temporary it makes last.
    /// </param>
    public void OpenPiece(int callee, int owner, bool gives)
    {
        var settlement = _settlements![callee]!;
        Close(settlement);
        settlement.Open = (owner, _session.Objects.TemporaryCount, gives);
    }

    /// <summary>
    /// Puts the temporaries the call's arguments and its target made in the order they are let go of when the statement is over, once they are evaluated.
    /// </summary>
    /// <param name="callee">The number of the call among the body's callees.</param>
    public void SettleCall(int callee)
    {
        var settlement = _settlements![callee]!;
        Close(settlement);
        ObjectReferences.SettleCall(_session, settlement.Start, settlement.Pieces.AsSpan(0, settlement.Count));
        settlement.Count = 0;
    }

    // what the open piece made, from its start to now; the call is given the last of it when the piece gives what it returned.
    private void Close(CallSettlement settlement)
    {
        if (settlement.Open.Owner < 0)
        {
            return;
        }

        var (owner, start, gives) = settlement.Open;
        var end = _session.Objects.TemporaryCount;
        settlement.Add(new TemporaryPiece(owner, start, end, gives && end > start ? end - 1 : -1));
        settlement.Open = (-1, 0, false);
    }

    private sealed class CallSettlement
    {
        public TemporaryPiece[] Pieces { get; private set; } = new TemporaryPiece[4];

        public int Count { get; set; }

        public int Start { get; private set; }

        public (int Owner, int Start, bool Gives) Open { get; set; } = (-1, 0, false);

        public void Begin(int start) => (Start, Count, Open) = (start, 0, (-1, 0, false));

        public void Add(TemporaryPiece piece)
        {
            if (Count == Pieces.Length)
            {
                var grown = new TemporaryPiece[Count * 2];
                Pieces.CopyTo(grown, 0);
                Pieces = grown;
            }

            Pieces[Count++] = piece;
        }
    }
}
