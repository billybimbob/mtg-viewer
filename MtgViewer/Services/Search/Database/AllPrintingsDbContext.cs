using Microsoft.EntityFrameworkCore;

namespace MtgViewer.Services.Search.Database;

public class AllPrintingsDbContext : DbContext
{
    public AllPrintingsDbContext(DbContextOptions<AllPrintingsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Card> Cards => Set<Card>();
    public virtual DbSet<CardIdentifier> CardIdentifiers => Set<CardIdentifier>();
    public DbSet<Set> Sets => Set<Set>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
    }
}
