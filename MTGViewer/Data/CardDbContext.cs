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
        public DbSet<Shared> Shares { get; set; }
        public DbSet<Deck> Decks { get; set; }

        public DbSet<CardAmount> Amounts { get; set; }

        public DbSet<Transfer> Transfers { get; set; }
        public DbSet<Suggestion> Suggestions { get; set; }
        public DbSet<Trade> Trades { get; set; }


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder
                .SelectConcurrencyToken(Database)

                .Entity<Location>(locBuild => locBuild
                    .HasDiscriminator(l => l.Type)
                        .HasValue<Location>(Discriminator.Invalid)
                        .HasValue<Shared>(Discriminator.Shared)
                        .HasValue<Deck>(Discriminator.Deck))

                .Entity<Transfer>(transferBuild =>
                {
                    transferBuild
                        .HasDiscriminator(t => t.Type)
                            .HasValue<Transfer>(Discriminator.Invalid)
                            .HasValue<Suggestion>(Discriminator.Suggestion)
                            .HasValue<Trade>(Discriminator.Trade);

                    transferBuild
                        .HasOne(t => t.Proposer)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);

                    transferBuild
                        .HasOne(t => t.Receiver)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);

                    transferBuild
                        .HasOne(t => t.To)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);

                    transferBuild
                        .HasMany(t => t.Decks)
                        .WithMany(d => d.Transfers);
                })
                
                .Entity<Trade>(tradeBuild =>
                {
                    tradeBuild
                        .HasOne(t => t.From)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);
                    
                    tradeBuild
                        .HasOne(t => t.TargetDeck)
                        .WithMany()
                        .OnDelete(DeleteBehavior.Cascade);
                });
        }

    }
}