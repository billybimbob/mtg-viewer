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
            modelBuilder
                .SelectConcurrencyToken(Database)

                .Entity<CardUser>(userBuild => 
                    userBuild.ToTable("AspNetUsers", t => t.ExcludeFromMigrations()))

                .Entity<Trade>(tradeBuild =>
                {
                    tradeBuild
                        .HasOne(t => t.Proposer)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);

                    tradeBuild
                        .HasOne(t => t.Receiver)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);

                    tradeBuild
                        .HasOne(t => t.From)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);

                    tradeBuild
                        .HasOne(t => t.To)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);
                });
        }

    }
}