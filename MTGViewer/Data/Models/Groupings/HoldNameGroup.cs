using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data;

/// <summary>Group of holds with the same card name</summary>
public class HoldNameGroup : IEnumerable<Hold>
{
    public HoldNameGroup(IEnumerable<Hold> holds)
    {
        _holds = new LinkedList<Hold>(holds);

        if (!_holds.Any())
        {
            throw new ArgumentException($"{nameof(holds)} is empty", nameof(holds));
        }

        if (_holds.Any(h => h.Card.Name != Name))
        {
            throw new ArgumentException("All cards do not match the name", nameof(holds));
        }

        if (_holds.Any(h => h.Card.ManaCost != ManaCost))
        {
            throw new ArgumentException("All cards do not match the mana cost", nameof(holds));
        }
    }

    public HoldNameGroup(params Hold[] holds)
        : this(holds.AsEnumerable())
    { }

    // guranteed >= 1 Holds in linkedlist
    private readonly LinkedList<Hold> _holds;

    private Hold First => _holds.First!.Value;

    public string Name => First.Card.Name;
    public string? ManaCost => First.Card.ManaCost;

    public IEnumerable<string> CardIds => _holds.Select(h => h.CardId);
    public IEnumerable<Card> Cards => _holds.Select(h => h.Card);

    public int Copies
    {
        get => _holds.Sum(h => h.Copies);
        set
        {
            var lastCycle = _holds.Last!.Value;
            int change = Copies - value;

            while (change < 0 || change > 0 && lastCycle.Copies > 0)
            {
                int mod = Math.Min(change, First.Copies);

                First.Copies -= mod;
                change -= mod;

                if (First.Copies == 0)
                {
                    // cycle hold
                    var firstLink = _holds.First!;
                    _holds.Remove(firstLink);
                    _holds.AddLast(firstLink);
                }
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Hold> GetEnumerator() => _holds.GetEnumerator();
}
