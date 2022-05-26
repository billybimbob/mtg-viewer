using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Areas.Identity.Data;

public class UserDbContext : IdentityDbContext<CardUser>, IDataProtectionKeyContext
{
    public UserDbContext(DbContextOptions<UserDbContext> options)
        : base(options)
    { }

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
}
