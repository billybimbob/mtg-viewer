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

        // public DbSet<Location> Locations => Set<Location>();
        public DbSet<Deck> Decks => Set<Deck>();

        public DbSet<Box> Boxes => Set<Box>();
        public DbSet<Bin> Bins => Set<Bin>();

        public DbSet<Suggestion> Suggestions => Set<Suggestion>();
        public DbSet<Exchange> Exchanges => Set<Exchange>();
        public DbSet<Change> Changes => Set<Change>();


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.SelectConcurrencyToken(Database);

            new CardConfiguration()
                .Configure(modelBuilder.Entity<Card>());

            new LocationConfiguration()
                .Configure(modelBuilder.Entity<Location>());
            
            // new BoxConfiguration()
            //     .Configure(modelBuilder.Entity<Box>());

            // new DeckConfiguration()
            //     .Configure(modelBuilder.Entity<Deck>());

            // new AmountConfiguration()
            //     .Configure(modelBuilder.Entity<CardAmount>());

            new ExchangeConfiguration()
                .Configure(modelBuilder.Entity<Exchange>());
            
            new ChangeConfiguration()
                .Configure(modelBuilder.Entity<Change>());
            
            new TransactionConfiguration()
                .Configure(modelBuilder.Entity<Transaction>());
        }
    }



    public class CardConfiguration : IEntityTypeConfiguration<Card>
    {
        public void Configure(EntityTypeBuilder<Card> builder)
        {
            builder.OwnsMany(c => c.Names);
            builder.OwnsMany(c => c.Colors);
            builder.OwnsMany(c => c.SuperTypes);
            builder.OwnsMany(c => c.Types);
            builder.OwnsMany(c => c.SubTypes);
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


    // public class BoxConfiguration : IEntityTypeConfiguration<Box>
    // {
    //     public void Configure(EntityTypeBuilder<Box> builder)
    //     {
    //         builder
    //             .HasMany(b => b.Cards)
    //             .WithOne(ba => ba.Box)
    //             .OnDelete(DeleteBehavior.Restrict);
    //     }
    // }


    // public class DeckConfiguration : IEntityTypeConfiguration<Deck>
    // {
    //     public void Configure(EntityTypeBuilder<Deck> builder)
    //     {
    //         builder
    //             .HasMany(d => d.Cards)
    //             .WithOne(da => da.Deck)
    //             .OnDelete(DeleteBehavior.Restrict);
    //     }
    // }


    // public class AmountConfiguration : IEntityTypeConfiguration<CardAmount>
    // {
    //     public void Configure(EntityTypeBuilder<CardAmount> builder)
    //     {
    //         builder
    //             .HasDiscriminator(ca => ca.Type)
    //                 .HasValue<CardAmount>(Discriminator.Invalid)
    //                 .HasValue<BoxAmount>(Discriminator.BoxAmount)
    //                 .HasValue<DeckAmount>(Discriminator.DeckAmount);
    //     }
    // }


    public class ExchangeConfiguration : IEntityTypeConfiguration<Exchange>
    {
        public void Configure(EntityTypeBuilder<Exchange> builder)
        {
            builder
                .HasOne(e => e.To)
                .WithMany(d => d.ExchangesTo)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne(e => e.From)
                .WithMany(d => d.ExchangesFrom)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }


    public class ChangeConfiguration : IEntityTypeConfiguration<Change>
    {
        public void Configure(EntityTypeBuilder<Change> builder)
        {
            builder
                .HasOne(c => c.To)
                .WithMany(l => l.ChangesTo)
                .OnDelete(DeleteBehavior.Cascade);

            builder
                .HasOne(c => c.From)
                .WithMany(l => l.ChangesFrom)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }


    public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
    {
        public void Configure(EntityTypeBuilder<Transaction> builder)
        {
            builder
                .Property(t => t.Applied)
                .HasDefaultValueSql("getdate()");
        }
    }


    public class SuggestionConfiguration : IEntityTypeConfiguration<Suggestion>
    {
        public void Configure(EntityTypeBuilder<Suggestion> builder)
        {
            builder
                .HasOne(s => s.To)
                .WithMany(d => d.Suggestions)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}