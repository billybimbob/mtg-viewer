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

        public DbSet<Deck> Decks => Set<Deck>();
        public DbSet<Box> Boxes => Set<Box>();
        public DbSet<Bin> Bins => Set<Bin>();

        public DbSet<Exchange> Exchanges => Set<Exchange>();
        public DbSet<Suggestion> Suggestions => Set<Suggestion>();

        public DbSet<Change> Changes => Set<Change>();
        public DbSet<Transaction> Transactions => Set<Transaction>();



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.SelectConcurrencyToken(Database);

            // modelBuilder.ApplyConfiguration(new CardConfiguration());
            modelBuilder.ApplyConfiguration(new LocationConfiguration());

            modelBuilder.ApplyConfiguration(new ExchangeConfiguration());
            modelBuilder.ApplyConfiguration(new SuggestionConfiguration());
            
            modelBuilder.ApplyConfiguration(new ChangeConfiguration());
            modelBuilder.ApplyConfiguration(new TransactionConfiguration());
        }
    }



    // internal class CardConfiguration : IEntityTypeConfiguration<Card>
    // {
    //     public void Configure(EntityTypeBuilder<Card> builder)
    //     {
    //         builder.OwnsMany(c => c.Names);
    //         builder.OwnsMany(c => c.Colors);
    //         builder.OwnsMany(c => c.SuperTypes);
    //         builder.OwnsMany(c => c.Types);
    //         builder.OwnsMany(c => c.SubTypes);
    //     }
    // }

    internal class LocationConfiguration : IEntityTypeConfiguration<Location>
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


    internal class ExchangeConfiguration : IEntityTypeConfiguration<Exchange>
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

            builder
                .Property(ex => ex.IsTrade)
                .UsePropertyAccessMode(PropertyAccessMode.Property);
        }
    }


    internal class ChangeConfiguration : IEntityTypeConfiguration<Change>
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


    internal class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
    {
        public void Configure(EntityTypeBuilder<Transaction> builder)
        {
            builder
                .Property(t => t.Applied)
                .HasDefaultValueSql("getdate()");
        }
    }


    internal class SuggestionConfiguration : IEntityTypeConfiguration<Suggestion>
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