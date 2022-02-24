using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using MTGViewer.Data.Concurrency;
using MTGViewer.Data.Internal;

namespace MTGViewer.Data;

public class CardDbContext : DbContext
{
    public CardDbContext(DbContextOptions<CardDbContext> options)
        : base(options)
    { }


    public DbSet<UserRef> Users => Set<UserRef>();

    public DbSet<Card> Cards => Set<Card>();

    public DbSet<Deck> Decks => Set<Deck>();
    public DbSet<Unclaimed> Unclaimed => Set<Unclaimed>();

    public DbSet<Box> Boxes => Set<Box>();
    public DbSet<Bin> Bins => Set<Bin>();

    public DbSet<Amount> Amounts => Set<Amount>();
    public DbSet<Want> Wants => Set<Want>();
    public DbSet<GiveBack> GiveBacks => Set<GiveBack>();

    public DbSet<Change> Changes => Set<Change>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder
            .SelectConcurrencyToken(Database)

            .ApplyConfiguration(new CardConfiguration())
            .ApplyConfiguration(new LocationConfiguration())

            .ApplyConfiguration(new OwnedConfiguration())
            .ApplyConfiguration(new DeckConfiguration())
            .ApplyConfiguration(new BoxConfiguration())

            .ApplyConfiguration(new QuantityConfiguration())
            .ApplyConfiguration(new SuggestionConfiguration(Database))

            .ApplyConfiguration(new ChangeConfiguration())
            .ApplyConfiguration(new TransactionConfiguration(Database));
    }
}



internal class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
    }
}


internal class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder
            .HasDiscriminator(l => l.Type)
                .HasValue<Location>(LocationType.Invalid)
                .HasValue<Owned>(LocationType.Invalid)
                .HasValue<Unclaimed>(LocationType.Unclaimed)
                .HasValue<Deck>(LocationType.Deck)
                .HasValue<Box>(LocationType.Box);

        builder
            .HasMany(l => l.Cards)
            .WithOne(a => a.Location)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


internal class OwnedConfiguration : IEntityTypeConfiguration<Owned>
{
    public void Configure(EntityTypeBuilder<Owned> builder)
    {
        builder
            .HasMany(o => o.Wants)
            .WithOne(w => (Owned)w.Location)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


internal class DeckConfiguration : IEntityTypeConfiguration<Deck>
{
    public void Configure(EntityTypeBuilder<Deck> builder)
    {
        builder
            .HasMany(d => d.GiveBacks)
            .WithOne(g => (Deck)g.Location)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(d => d.TradesTo)
            .WithOne(t => t.To)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(d => d.TradesFrom)
            .WithOne(t => t.From)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


internal class BoxConfiguration : IEntityTypeConfiguration<Box>
{
    public void Configure(EntityTypeBuilder<Box> builder)
    {
        builder
            .HasOne(b => b.Bin)
            .WithMany(b => b.Boxes)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .Property(b => b.IsExcess)
            .HasDefaultValue(false);
    }
}


internal class QuantityConfiguration : IEntityTypeConfiguration<Quantity>
{
    public void Configure(EntityTypeBuilder<Quantity> builder)
    {
        builder
            .HasDiscriminator(cq => cq.Type)
            .HasValue<Quantity>(QuantityType.Invalid)
            .HasValue<Amount>(QuantityType.Amount)
            .HasValue<Want>(QuantityType.Want)
            .HasValue<GiveBack>(QuantityType.GiveBack);
    }
}


internal class ChangeConfiguration : IEntityTypeConfiguration<Change>
{
    public void Configure(EntityTypeBuilder<Change> builder)
    {
        builder
            .HasOne(c => c.To)
            .WithMany()
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(c => c.From)
            .WithMany()
            .OnDelete(DeleteBehavior.SetNull);
    }
}


internal class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    private readonly DatabaseFacade _database;

    public TransactionConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder
            .Property(t => t.AppliedAt)
            .HasDefaultValueSql(_database.GetUtcTime());

        builder
            .HasMany(t => t.Changes)
            .WithOne(c => c.Transaction)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


internal class SuggestionConfiguration : IEntityTypeConfiguration<Suggestion>
{
    private readonly DatabaseFacade _database;

    public SuggestionConfiguration(DatabaseFacade database)
    {
        _database = database;
    }

    
    public void Configure(EntityTypeBuilder<Suggestion> builder)
    {
        builder
            .Property(s => s.SentAt)
            .HasDefaultValueSql(_database.GetUtcTime());

        builder
            .HasOne(s => s.To)
            .WithMany()
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(s => s.Receiver)
            .WithMany()
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasOne(s => s.Card)
            .WithMany(c => c.Suggestions)
            .OnDelete(DeleteBehavior.Cascade);
    }
}