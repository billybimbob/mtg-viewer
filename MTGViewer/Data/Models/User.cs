using System.ComponentModel.DataAnnotations;
using MTGViewer.Areas.Identity.Data;

namespace MTGViewer.Data;

public class UserRef
{
    public UserRef()
    { }

    public UserRef(CardUser user)
    {
        Id = user.Id;
        Name = user.Name;
    }

    [Key]
    public string Id { get; init; } = null!;

    [StringLength(256)]
    public string Name { get; set; } = null!;
}