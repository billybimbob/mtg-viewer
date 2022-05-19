using System;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace MTGViewer.Data.Infrastructure;

public sealed class BinDto
{
    private readonly BoxDto? _box;
    private string? _name;

    public BinDto()
    {
        _box = null;
    }

    public BinDto(BoxDto box)
    {
        _box = box;
    }

    public bool IsEdit => Id != default;

    public int Id { get; set; }

    [Required(ErrorMessage = "Bin Name is Missing")]
    [StringLength(10, ErrorMessage = "Bin Name has a character limit of 10")]
    public string? Name
    {
        get => _name;
        set
        {
            if (_box is null || _box.IsEdit || !IsEdit)
            {
                _name = value;
            }
        }
    }

    public void Update(BinDto? bin)
    {
        Id = bin?.Id ?? default;
        _name = bin?.Name;
    }

    public static string PropertyId<T>(Expression<Func<BinDto, T>> property)
    {
        if (property.Body is not MemberExpression expression)
        {
            return string.Empty;
        }

        return $"{nameof(BinDto)}-{expression.Member.Name}";
    }
}
