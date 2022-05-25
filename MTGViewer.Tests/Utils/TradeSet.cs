using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using MTGViewer.Data;

namespace MTGViewer.Tests.Utils;

/// <summary>Group of trades with either the same To or From deck</summary>
public class TradeSet : IEnumerable<Trade>
{
    private readonly IReadOnlyCollection<Trade> _trades;
    private readonly bool _useToTarget;
    private readonly Trade _first;

    public TradeSet(IEnumerable<Trade> trades, bool useToTarget)
    {
        _trades = trades.ToList();

        if (!_trades.Any())
        {
            throw new ArgumentException("The trade group is empty", nameof(trades));
        }

        _first = _trades.First();

        if (_trades.Any(t => t.To != _first.To && t.From != _first.From))
        {
            throw new ArgumentException("All trade destinations are not the same", nameof(trades));
        }

        _useToTarget = (useToTarget, _first) switch
        {
            (true, { To: not null }) => true,
            (false, { From: not null }) => false,
            _ => throw new InvalidOperationException("Trade target not specified")
        };
    }

    public Deck Target => _useToTarget ? _first.To : _first.From;
    public int TargetId => Target?.Id
        ?? (_useToTarget ? _first.ToId : _first.FromId);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Trade> GetEnumerator() => _trades.GetEnumerator();

}
