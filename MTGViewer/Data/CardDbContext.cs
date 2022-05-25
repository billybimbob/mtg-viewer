using Microsoft.EntityFrameworkCore;

using MTGViewer.Data.Configuration;

namespace MTGViewer.Data;

public partial class CardDbContext : DbContext
{
    public CardDbContext(DbContextOptions<CardDbContext> options)
        : base(options)
    { }

    public DbSet<UserRef> Users => Set<UserRef>();

    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Deck> Decks => Set<Deck>();
    public DbSet<Unclaimed> Unclaimed => Set<Unclaimed>();

    public DbSet<Excess> Excess => Set<Excess>();
    public DbSet<Box> Boxes => Set<Box>();
    public DbSet<Bin> Bins => Set<Bin>();

    public DbSet<Hold> Holds => Set<Hold>();
    public DbSet<Want> Wants => Set<Want>();
    public DbSet<Giveback> Givebacks => Set<Giveback>();

    public DbSet<Change> Changes => Set<Change>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        _ = SelectConcurrencyToken(modelBuilder)

            .ApplyConfiguration(new CardConfiguration())
            .ApplyConfiguration(new LocationConfiguration())

            .ApplyConfiguration(new TheoryCraftConfiguration())
            .ApplyConfiguration(new DeckConfiguration())
            .ApplyConfiguration(new BoxConfiguration())

            .ApplyConfiguration(new QuantityConfiguration())

            .ApplyConfiguration(new ChangeConfiguration())
            .ApplyConfiguration(new TransactionConfiguration(Database))

            .ApplyConfiguration(new SuggestionConfiguration(Database));
    }
}
