using System;
using System.ComponentModel.DataAnnotations;

using MtgViewer.Data;

namespace MtgViewer.Services.Search;

public sealed record CardSearch : IMtgSearch
{
    public static IMtgSearch Empty { get; } = new CardSearch();

    public bool IsEmpty => this == (Empty as CardSearch);

    private string? _name;
    public string? Name
    {
        get => _name;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value is null or { Length: <= 50 })
            {
                _name = value;
            }
        }
    }

    private int? _manaValue;

    [Display(Name = "Mana Value")]
    public int? ManaValue
    {
        get => _manaValue;
        set
        {
            if (value is null or (>= 0 and < 1_000_000))
            {
                _manaValue = value;
            }
        }
    }

    private Color _colors;
    public Color Colors
    {
        get => _colors;
        set => _colors = value & Symbol.Rainbow;
    }

    public void ToggleColors(Color toggle) => Colors ^= toggle;

    private Rarity? _rarity;
    public Rarity? Rarity
    {
        get => _rarity;
        set
        {
            if (value is null || (value is Rarity r && Enum.IsDefined(r)))
            {
                _rarity = value;
            }
        }
    }

    private string? _setName;

    [Display(Name = "Set Name")]
    public string? SetName
    {
        get => _setName;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value is null or { Length: <= 30 })
            {
                _setName = value;
            }
        }
    }

    private string? _type;

    [Display(Name = "Type(s)")]
    public string? Types
    {
        get => _type;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value is null or { Length: <= 60 })
            {
                _type = value;
            }
        }
    }

    private string? _artist;
    public string? Artist
    {
        get => _artist;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value is null or { Length: <= 30 })
            {
                _artist = value;
            }
        }
    }

    private string? _power;
    public string? Power
    {
        get => _power;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value is null or { Length: <= 5 })
            {
                _power = value;
            }
        }
    }

    private string? _toughness;
    public string? Toughness
    {
        get => _toughness;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value is null or { Length: <= 5 })
            {
                _toughness = value;
            }
        }
    }

    private string? _loyalty;

    [Display(Name = "Starting Loyalty")]
    public string? Loyalty
    {
        get => _loyalty;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value is null or { Length: <= 5 })
            {
                _loyalty = value;
            }
        }
    }

    private string? _text;

    [Display(Name = "Oracle Text")]
    public string? Text
    {
        get => _text;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value is null or { Length: <= 40 })
            {
                _text = value;
            }
        }
    }

    private string? _flavor;

    [Display(Name = "Flavor Text")]
    public string? Flavor
    {
        get => _flavor;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                value = null;
            }

            if (value is null or { Length: <= 40 })
            {
                _flavor = value;
            }
        }
    }

    private int _page;
    public int Page
    {
        get => _page;
        set
        {
            if (value >= 0)
            {
                _page = value;
            }
        }
    }

    private int _pageSize;

    [Range(0, int.MaxValue)]
    public int PageSize
    {
        get => _pageSize;
        set
        {
            if (value >= 0)
            {
                _pageSize = value;
            }
        }
    }
}
