using System;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace MTGViewer.Data.Infrastructure;

public sealed class BoxDto
{
    public BoxDto()
    {
        Bin = new BinDto(this);
    }

    public int Id { get; set; }

    public bool IsEdit => Id != default;

    [Required(ErrorMessage = "Box Name is Missing")]
    [StringLength(20, ErrorMessage = "Box Name has a character limit of 20")]
    public string? Name { get; set; }

    [StringLength(40, ErrorMessage = "Appearance has a character limit of 40")]
    public string? Appearance { get; set; }

    [Range(10, 10_000, ErrorMessage = "Capacity must be Between 10 and 10,000")]
    public int Capacity { get; set; }

    [ValidateComplexType]
    public BinDto Bin { get; init; }

    public static string PropertyId<T>(Expression<Func<BoxDto, T>> property)
    {
        if (property.Body is not MemberExpression expression)
        {
            return string.Empty;
        }

        return $"{nameof(BoxDto)}-{expression.Member.Name}";
    }
}
