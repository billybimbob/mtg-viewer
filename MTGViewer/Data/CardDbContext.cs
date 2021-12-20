using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Infrastructure;

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
        builder
            .OwnsMany(c => c.Names)
            .HasKey(n => new { n.Value, n.CardId });

        builder
            .Navigation(c => c.Names)
            .AutoInclude(false);

        builder
            .OwnsMany(c => c.Colors)
            .HasKey(cl => new { cl.Name, cl.CardId });

        builder
            .Navigation(c => c.Colors)
            .AutoInclude(false);

        builder
            .OwnsMany(c => c.Supertypes)
            .HasKey(sp => new { sp.Name, sp.CardId });

        builder
            .Navigation(c => c.Supertypes)
            .AutoInclude(false);

        builder
            .OwnsMany(c => c.Types)
            .HasKey(ty => new { ty.Name, ty.CardId });

        builder
            .Navigation(c => c.Types)
            .AutoInclude(false);

        builder
            .OwnsMany(c => c.Subtypes)
            .HasKey(sb => new { sb.Name, sb.CardId });

        builder
            .Navigation(c => c.Subtypes)
            .AutoInclude(false);
    }
}


internal class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder
            .HasDiscriminator(l => l.Type)
                .HasValue<Location>(Discriminator.Invalid)
                .HasValue<Owned>(Discriminator.Invalid)
                .HasValue<Unclaimed>(Discriminator.Unclaimed)
                .HasValue<Deck>(Discriminator.Deck)
                .HasValue<Box>(Discriminator.Box);

        builder
            .HasMany(l => l.Cards)
            .WithOne(ca => ca.Location)
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
            .OnDelete(DeleteBehavior.ClientCascade);

        builder
            .HasMany(d => d.TradesFrom)
            .WithOne(t => t.From)
            .OnDelete(DeleteBehavior.ClientCascade);
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
    }
}


internal class QuantityConfiguration : IEntityTypeConfiguration<Quantity>
{
    public void Configure(EntityTypeBuilder<Quantity> builder)
    {
        builder
            .HasDiscriminator(cq => cq.Type)
            .HasValue<Quantity>(Discriminator.Invalid)
            .HasValue<Amount>(Discriminator.Amount)
            .HasValue<Want>(Discriminator.Want)
            .HasValue<GiveBack>(Discriminator.GiveBack);
    }
}


internal class ChangeConfiguration : IEntityTypeConfiguration<Change>
{
    public void Configure(EntityTypeBuilder<Change> builder)
    {
        builder
            .HasOne(c => c.To)
            .WithMany()
            .OnDelete(DeleteBehavior.ClientSetNull);

        builder
            .HasOne(c => c.From)
            .WithMany()
            .OnDelete(DeleteBehavior.ClientSetNull);
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
            .HasDefaultValueSql(_database.GetCurrentTime());

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
            .HasDefaultValueSql(_database.GetCurrentTime());

        builder
            .HasOne(s => s.To)
            .WithMany()
            .OnDelete(DeleteBehavior.ClientSetNull);

        builder
            .HasOne(s => s.Receiver)
            .WithMany()
            .OnDelete(DeleteBehavior.Cascade);
    }
}