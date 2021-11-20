using System.ComponentModel.DataAnnotations;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Data;

public class UserRef
{
    public UserRef()
    {
        Id = null!;
        Name = null!;
    }

    public UserRef(CardUser user)
    {
        Id = user.Id;
        Name = user.Name ?? string.Empty;
    }

    [Key]
    public string Id { get; init; }

    [StringLength(256)]
    public string Name { get; init; }
}