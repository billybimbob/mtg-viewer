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
        Name = user.DisplayName;
    }

    [Key]
    public string Id { get; init; } = default!;

    [StringLength(256, MinimumLength = 1)]
    public string Name { get; set; } = default!;

    public bool ResetRequested { get; set;  }
}