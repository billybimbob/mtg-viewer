using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MTGViewer.Areas.Identity.Data;

public class UserDbContext : IdentityDbContext<CardUser>
{
    public UserDbContext(DbContextOptions<UserDbContext> options)
        : base(options)
    { }
}