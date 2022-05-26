using MtgViewer.Data.Treasury.Handlers;

namespace MtgViewer.Data.Treasury;

internal sealed partial class ExchangeContext
{
    public void TakeExact()
        => new ExactTake(this).ApplyTakes();

    public void TakeApproximate()
        => new ApproximateTake(this).ApplyTakes();

    public void ReturnExact()
        => new ExactReturn(this).ApplyReturns();

    public void ReturnApproximate()
        => new ApproximateReturn(this).ApplyReturns();

    public void ReturnGuess()
        => new GuessReturn(this).ApplyReturns();
}
