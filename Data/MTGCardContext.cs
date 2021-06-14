using Microsoft.EntityFrameworkCore;
using MTGViewer.Models;


public class MTGCardContext : DbContext
{
    public MTGCardContext(DbContextOptions<MTGCardContext> options)
        : base(options)
    {
    }

    public DbSet<Card> Cards { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Deck> Decks { get; set; }

    public DbSet<Color> Colors { get; set; }
    public DbSet<Type> Types { get; set; }
    public DbSet<SubType> SubTypes { get; set; }
    public DbSet<SuperType> SuperTypes { get; set; }


    // protected override void OnModelCreating(ModelBuilder builder)
    // {

    // }

}
