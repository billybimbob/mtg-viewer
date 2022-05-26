using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MtgViewer.Data.Configuration;

internal class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        _ = builder
            .Navigation(c => c.Flip)
            .AutoInclude(false);
    }
}

internal class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        _ = builder
            .HasDiscriminator(l => l.Type)
            .HasValue<Location>(LocationType.Invalid)

            .HasValue<Theorycraft>(LocationType.Invalid)
            .HasValue<Deck>(LocationType.Deck)
            .HasValue<Unclaimed>(LocationType.Unclaimed)

            .HasValue<Storage>(LocationType.Invalid)
            .HasValue<Box>(LocationType.Box)
            .HasValue<Excess>(LocationType.Excess);

        _ = builder
            .HasMany(l => l.Holds)
            .WithOne(h => h.Location)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal class TheoryCraftConfiguration : IEntityTypeConfiguration<Theorycraft>
{
    public void Configure(EntityTypeBuilder<Theorycraft> builder)
    {
        _ = builder
            .HasMany(t => t.Wants)
            .WithOne(w => w.Location)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal class DeckConfiguration : IEntityTypeConfiguration<Deck>
{
    public void Configure(EntityTypeBuilder<Deck> builder)
    {
        _ = builder
            .HasMany(d => d.Givebacks)
            .WithOne(g => g.Location)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder
            .HasMany(d => d.TradesTo)
            .WithOne(t => t.To)
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder
            .HasMany(d => d.TradesFrom)
            .WithOne(t => t.From)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal class BoxConfiguration : IEntityTypeConfiguration<Box>
{
    public void Configure(EntityTypeBuilder<Box> builder)
    {
        _ = builder
            .HasOne(b => b.Bin)
            .WithMany(b => b.Boxes)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal class QuantityConfiguration : IEntityTypeConfiguration<Quantity>
{
    public void Configure(EntityTypeBuilder<Quantity> builder)
    {
        _ = builder
            .HasDiscriminator(q => q.Type)
            .HasValue<Quantity>(QuantityType.Invalid)

            .HasValue<Hold>(QuantityType.Hold)
            .HasValue<Giveback>(QuantityType.Giveback)
            .HasValue<Want>(QuantityType.Want);
    }
}

internal class ChangeConfiguration : IEntityTypeConfiguration<Change>
{
    public void Configure(EntityTypeBuilder<Change> builder)
    {
        _ = builder
            .HasOne(c => c.To)
            .WithMany()
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder
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
        _ = builder
            .Property(t => t.AppliedAt)
            .HasDefaultValueSql(_database.GetUtcTime());

        _ = builder
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
        _ = builder
            .Property(s => s.SentAt)
            .HasDefaultValueSql(_database.GetUtcTime());

        _ = builder
            .HasOne(s => s.To)
            .WithMany()
            .OnDelete(DeleteBehavior.SetNull);

        _ = builder
            .HasOne(s => s.Receiver)
            .WithMany()
            .OnDelete(DeleteBehavior.Cascade);

        _ = builder
            .HasOne(s => s.Card)
            .WithMany(c => c.Suggestions)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
