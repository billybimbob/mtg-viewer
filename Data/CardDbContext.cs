using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Concurrency;
using MTGViewer.Areas.Identity.Data;


namespace MTGViewer.Data
{
    public class CardDbContext : DbContext
    {
        public CardDbContext(DbContextOptions<CardDbContext> options)
            : base(options)
        { }

        public DbSet<Card> Cards { get; set; }
        public DbSet<Location> Locations { get; set; }
        public DbSet<CardAmount> Amounts { get; set; }
        public DbSet<Trade> Trades { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<CardUser>()
                .ToTable("AspNetUsers", t => t.ExcludeFromMigrations());

            if (Database.IsSqlite())
            {
                foreach(var concurrentType in ConcurrencyExtensions.GetConcurrentTypes())
                {
                    modelBuilder.Entity(concurrentType)
                        .IgnoreExceptToken(c => c.LiteToken);
                }
            }
            else if (Database.IsSqlServer())
            {
                foreach(var concurrentType in ConcurrencyExtensions.GetConcurrentTypes())
                {
                    modelBuilder.Entity(concurrentType)
                        .IgnoreExceptToken(c => c.SqlToken);
                }
            }

            modelBuilder.Entity<Trade>(tradeBuild =>
            {
                tradeBuild
                    .HasOne(t => t.Proposer)
                    .WithMany();

                tradeBuild
                    .HasOne(t => t.Receiver)
                    .WithMany();

                tradeBuild
                    .HasOne(t => t.From)
                    .WithOne()
                    .OnDelete(DeleteBehavior.Cascade);

                tradeBuild
                    .HasOne(t => t.To)
                    .WithMany();
            });
        }

    }
}