using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using Moq;
using Xunit;

using MTGViewer.Areas.Identity.Data;
using MTGViewer.Data;

using MTGViewer.Services;
using MTGViewer.Pages.Decks;
using MTGViewer.Tests.Utils;


namespace MTGViewer.Tests.Pages.Decks
{
    public class DeleteTests : IAsyncLifetime
    {
        private readonly CardDbContext _dbContext;
        private readonly UserManager<CardUser> _userManager;
        private readonly TestDataGenerator _testGen;

        private readonly DeleteModel _deleteModel;


        public DeleteTests(
            CardDbContext dbContext,
            UserManager<CardUser> userManager,
            TestDataGenerator testGen,
            ISharedStorage sharedStorage)
        {
            _dbContext = dbContext;
            _userManager = userManager;
            _testGen = testGen;

            var logger = Mock.Of<ILogger<DeleteModel>>();

            _deleteModel = new(_userManager, _dbContext, sharedStorage, logger);
        }


        public Task InitializeAsync() => _testGen.SeedAsync();

        public Task DisposeAsync() => _testGen.ClearAsync();


        private IQueryable<Deck> Deck(Deck deck) =>
            _dbContext.Decks
                .Where(d => d.Id == deck.Id)
                .AsNoTracking();


        private IQueryable<CardAmount> DeckCards(Deck deck) =>
            _dbContext.Amounts
                .Where(ca => ca.LocationId == deck.Id)
                .AsNoTracking();



        [Fact]
        public async Task OnPost_WrongUser_NoChange()
        {
            // Arrange
            var deck = await _testGen.CreateDeckAsync();
            var wrongUser = await _dbContext.Users.FirstAsync(u => u.Id != deck.OwnerId);

            await _deleteModel.SetModelContextAsync(_userManager, wrongUser.Id);

            // Act
            var result = await _deleteModel.OnPostAsync(deck.Id);
            var deckAfter = await Deck(deck).SingleOrDefaultAsync();

            // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(deckAfter);
        }


        [Fact]
        public async Task OnPost_InvalidDeck_NoChange()
        {
            // Arrange
            var deck = await _testGen.CreateDeckAsync();

            await _deleteModel.SetModelContextAsync(_userManager, deck.OwnerId);

            var wrongDeck = -1;

            // Act
            var result = await _deleteModel.OnPostAsync(wrongDeck);
            var deckAfter = await Deck(deck).SingleOrDefaultAsync();

            // // Assert
            Assert.IsType<RedirectToPageResult>(result);
            Assert.NotNull(deckAfter);
        }


        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(10)]
        public async Task OnPost_ValidDeck_ReturnsCards(int numCopies)
        {
            // Arrange
            var deck = await _testGen.CreateDeckAsync(numCopies);

            await _deleteModel.SetModelContextAsync(_userManager, deck.OwnerId);

            var deckCards = await DeckCards(deck)
                .Select(ca => ca.CardId)
                .ToListAsync();

            var boxQuery = _dbContext.Amounts
                .Where(ca => deckCards.Contains(ca.CardId))
                .Select(ca => ca.Amount);

            // Act
            var boxBefore = await boxQuery.ToListAsync();
            var result = await _deleteModel.OnPostAsync(deck.Id);

            var boxAfter = await boxQuery.ToListAsync();
            var deckAfter = await Deck(deck).SingleOrDefaultAsync();

            var boxChange = boxBefore.Zip(boxAfter, (before, after) => (before, after));

            // Assert
            Assert.IsType<RedirectToPageResult>(result);

            Assert.Null(deckAfter);
            Assert.NotEqual(boxBefore, boxAfter);
            Assert.All(boxChange, ba => Assert.True(ba.before <= ba.after));
        }
    }
}