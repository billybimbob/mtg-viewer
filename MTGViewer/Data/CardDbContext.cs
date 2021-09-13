using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;


namespace MTGViewer.Data
{
    public class CardDbContext : DbContext
    {
        public CardDbContext(DbContextOptions<CardDbContext> options)
            : base(options)
        { }


        public DbSet<UserRef> Users => Set<UserRef>();
        public DbSet<Card> Cards => Set<Card>();
        public DbSet<CardAmount> Amounts => Set<CardAmount>();

        public DbSet<Location> Locations => Set<Location>();
        public DbSet<Deck> Decks => Set<Deck>();

        public DbSet<Box> Boxes => Set<Box>();
        public DbSet<Bin> Bins => Set<Bin>();

        public DbSet<Transfer> Transfers => Set<Transfer>();
        public DbSet<Suggestion> Suggestions => Set<Suggestion>();
        public DbSet<Trade> Trades => Set<Trade>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.SelectConcurrencyToken(Database);

            new LocationConfiguration()
                .Configure(modelBuilder.Entity<Location>());
            
            new TransferConfiguration()
                .Configure(modelBuilder.Entity<Transfer>());

            new TradeConfiguration()
                .Configure(modelBuilder.Entity<Trade>());
        }
    }



    public class LocationConfiguration : IEntityTypeConfiguration<Location>
    {
        public void Configure(EntityTypeBuilder<Location> builder)
        {
            builder
                .HasDiscriminator(l => l.Type)
                    .HasValue<Location>(Discriminator.Invalid)
                    .HasValue<Box>(Discriminator.Box)
                    .HasValue<Deck>(Discriminator.Deck);

            builder
                .HasMany(l => l.Cards)
                .WithOne(ca => ca.Location)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }


    public class TransferConfiguration : IEntityTypeConfiguration<Transfer>
    {
        public void Configure(EntityTypeBuilder<Transfer> builder)
        {
            builder
                .HasDiscriminator(t => t.Type)
                    .HasValue<Transfer>(Discriminator.Invalid)
                    .HasValue<Suggestion>(Discriminator.Suggestion)
                    .HasValue<Trade>(Discriminator.Trade);

            builder
                .HasOne(t => t.Proposer)
                .WithMany()
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne(t => t.Receiver)
                .WithMany()
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne(t => t.To)
                .WithMany(d => d.ToRequests)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }


    public class TradeConfiguration : IEntityTypeConfiguration<Trade>
    {
        public void Configure(EntityTypeBuilder<Trade> builder)
        {
            builder
                .HasOne(t => t.From)
                .WithMany(d => d.FromRequests)
                .OnDelete(DeleteBehavior.Cascade);
            
            builder
                .HasOne(t => t.TargetDeck)
                .WithMany()
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}