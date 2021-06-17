using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Areas.Identity.Data;


public class MTGCardContext : IdentityDbContext<CardUser>
{
    public MTGCardContext(DbContextOptions<MTGCardContext> options)
        : base(options)
    {
    }

    public DbSet<Card> Cards { get; set; }
    public DbSet<Location> Locations { get; set; }

    // public DbSet<Color> Colors { get; set; }
    // public DbSet<Type> Types { get; set; }
    // public DbSet<SubType> SubTypes { get; set; }
    // public DbSet<SuperType> SuperTypes { get; set; }


    // protected override void OnModelCreating(ModelBuilder builder)
    // {
    //     base.OnModelCreating(builder);
    // }

}