using Microsoft.EntityFrameworkCore;
using MTGViewer.Models;

public class MTGCardContext : DbContext
{
    public MTGCardContext(DbContextOptions<MTGCardContext> options)
        : base(options)
    {
    }

    public DbSet<Card> Cards { get; set; }
}
