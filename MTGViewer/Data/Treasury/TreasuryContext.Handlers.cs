using System.Collections.Generic;
using MTGViewer.Data.Treasury.Handlers;

namespace MTGViewer.Data.Treasury;

internal sealed partial class TreasuryContext
{
    public void AddExact(IEnumerable<CardRequest> requests)
        => new ExactAdd(this, requests).AddCopies();

    public void AddApproximate(IEnumerable<CardRequest> requests)
        => new ApproximateAdd(this, requests).AddCopies();

    public void AddGuess(IEnumerable<CardRequest> requests)
        => new GuessAdd(this, requests).AddCopies();

    public void ReduceExcessExact()
        => new ExactExcess(this).TransferExcess();

    public void ReduceExcessApproximate()
        => new ApproximateExcess(this).TransferExcess();

    public void ReduceOverflowExact()
        => new ExactOverflow(this).TransferOverflow();

    public void ReduceOverflowApproximate()
        => new ApproximateOverflow(this).TransferOverflow();
}
