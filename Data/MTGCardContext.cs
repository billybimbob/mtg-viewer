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
    public DbSet<CardAmount> Amounts { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CardAmount>()
            .HasKey(ca => new { ca.CardId, ca.LocationId, ca.IsRequest });
    }

}