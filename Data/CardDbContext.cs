using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Data.Concurrency;
using MTGViewer.Areas.Identity.Data;


public class CardDbContext : DbContext
{
    public CardDbContext(DbContextOptions<CardDbContext> options)
        : base(options)
    {
    }

    public DbSet<Card> Cards { get; set; }
    public DbSet<Location> Locations { get; set; }
    public DbSet<CardAmount> Amounts { get; set; }


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CardUser>()
            .ToTable("AspNetUsers", t => t.ExcludeFromMigrations());

        if (Database.IsSqlite())
        {
            foreach(var concurrentType in Concurrency.GetConcurrentTypes())
            {
                modelBuilder.Entity(concurrentType)
                    .IgnoreExceptToken(c => c.LiteToken);
            }
        }
        else if (Database.IsSqlServer())
        {
            foreach(var concurrentType in Concurrency.GetConcurrentTypes())
            {
                modelBuilder.Entity(concurrentType)
                    .IgnoreExceptToken(c => c.SqlToken);
            }
        }

        modelBuilder.Entity<CardAmount>()
            .HasKey(ca => new { ca.CardId, ca.LocationId, ca.IsRequest });
    }

}