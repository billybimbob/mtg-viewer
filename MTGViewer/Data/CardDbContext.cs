using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Concurrency;
using MTGViewer.Areas.Identity.Data;


namespace MTGViewer.Data
{
    public class CardDbContext : IdentityDbContext<CardUser>
    {
        public CardDbContext(DbContextOptions<CardDbContext> options)
            : base(options)
        { }

        public DbSet<Card> Cards { get; set; }

        public DbSet<Location> Locations { get; set; }
        public DbSet<Deck> Decks { get; set; }

        public DbSet<CardAmount> Amounts { get; set; }

        public DbSet<Suggestion> Suggestions { get; set; }
        public DbSet<Trade> Trades { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder
                .SelectConcurrencyToken(Database)

                .Entity<Location>(locBuild => locBuild
                    .HasDiscriminator<bool>(l => l.IsShared)
                    .HasValue<Location>(true)
                    .HasValue<Deck>(false))

                .Entity<Suggestion>(suggestBuild =>
                {
                    suggestBuild
                        .HasDiscriminator<bool>(s => s.IsSuggestion)
                        .HasValue<Suggestion>(true)
                        .HasValue<Trade>(false);

                    suggestBuild
                        .HasOne(s => s.Proposer)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);

                    suggestBuild
                        .HasOne(s => s.Receiver)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);

                    suggestBuild
                        .HasOne(s => s.To)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);
                })
                
                .Entity<Trade>(tradeBuild =>
                {
                    tradeBuild
                        .HasOne(t => t.From)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);
                });
        }

    }
}