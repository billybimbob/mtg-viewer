using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data;
using MTGViewer.Data.Concurrency;
using MTGViewer.Areas.Identity.Data;


public class CardDbContext : IdentityDbContext<CardUser>
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
        base.OnModelCreating(modelBuilder);

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