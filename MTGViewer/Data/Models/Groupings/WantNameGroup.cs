using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace MTGViewer.Data;

/// <summary>Group of wants with the same card name</summary>
public class WantNameGroup : IEnumerable<Want>
{
    public WantNameGroup(IEnumerable<Want> wants)
    {
        _wants = new LinkedList<Want>(wants);

        if (!_wants.Any())
        {
            throw new ArgumentException("The exchanges are empty", nameof(wants));
        }

        if (_wants.Any(w => w.Card.Name != Name))
        {
            throw new ArgumentException("All exchanges do not match the name", nameof(wants));
        }

        if (_wants.Any(w => w.Card.ManaCost != ManaCost))
        {
            throw new ArgumentException("All exchanges do not match the mana cost", nameof(wants));
        }
    }

    public WantNameGroup(params Want[] wants)
        : this(wants.AsEnumerable())
    { }

    // guranteed >= 1 Want in linkedlist
    private readonly LinkedList<Want> _wants;

    private Want First => _wants.First!.Value;

    public string Name => First.Card.Name;
    public string? ManaCost => First.Card.ManaCost;

    public IEnumerable<string> CardIds => _wants.Select(w => w.CardId);
    public IEnumerable<Card> Cards => _wants.Select(w => w.Card);

    public int Copies
    {
        get => _wants.Sum(w => w.Copies);
        set
        {
            int change = Copies - value;
            while (change < 0 || change > 0 && First.Copies > 0)
            {
                int mod = Math.Min(change, First.Copies);

                First.Copies -= mod;
                change -= mod;

                if (First.Copies == 0)
                {
                    var firstLink = _wants.First!;
                    _wants.Remove(firstLink);
                    _wants.AddLast(firstLink);
                }
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public IEnumerator<Want> GetEnumerator() => _wants.GetEnumerator();
}
