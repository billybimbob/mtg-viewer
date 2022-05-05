using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using MTGViewer.Data;
using MTGViewer.Services.Infrastructure;

namespace MTGViewer.Services;

public class BackupFactory
{
    private readonly CardDbContext _dbContext;

    public BackupFactory(CardDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Stream> GetUserBackupAsync(string userId, CancellationToken cancel = default)
    {
        var stream = CardStream.User(_dbContext, userId);

        return await SerializeAsync(stream, cancel);
    }

    public async Task<Stream> GetTreasuryBackupAsync(CancellationToken cancel = default)
    {
        var stream = CardStream.Treasury(_dbContext);

        return await SerializeAsync(stream, cancel);
    }

    public async Task<Stream> GetDefaultBackupAsync(CancellationToken cancel = default)
    {
        var stream = CardStream.Default(_dbContext);

        return await SerializeAsync(stream, cancel);
    }

    private static async Task<Stream> SerializeAsync(CardStream stream, CancellationToken cancel)
    {
        var serializeOptions = new JsonSerializerOptions
        {
            ReferenceHandler = ReferenceHandler.Preserve
        };

        // copy all data to memory, keep eye on
        // could possibly use temp file?

        var utf8Stream = new MemoryStream();

        var data = await CardData.FromStreamAsync(stream, cancel);

        await JsonSerializer.SerializeAsync(utf8Stream, data, serializeOptions, cancel);

        utf8Stream.Position = 0;

        return utf8Stream;
    }
}
