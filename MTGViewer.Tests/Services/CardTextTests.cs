using System.Linq;
using Xunit;
using MTGViewer.Services;

#nullable enable

namespace MTGViewer.Tests.Services
{
    public class CardTextTests
    {
        private readonly CardText _cardText;

        public CardTextTests(CardText cardText)
        {
            _cardText = cardText;
        }


        [Theory]
        [InlineData("{B}", "B")]
        [InlineData("{G}", "G")]
        [InlineData("{10}", "10")]
        public void FindMana_SingleMana_SingleSymbol(string cost, string mana)
        {
            var parsedSymbols = _cardText.FindMana(cost);
            var parsedMana = parsedSymbols[0];

            Assert.Single(parsedSymbols);
            Assert.Equal(mana, parsedMana.Value);
        }


        [Fact]
        public void FindMana_ManaCost_ManaSymbolArray()
        {
            var manaCost = "{3}{W}{W}";
            var manaSymbols = new [] { "3", "W", "W" };

            var parsedMana = _cardText.FindMana(manaCost).Select(m => m.Value);

            Assert.Equal(manaSymbols, parsedMana);
        }


        [Fact]
        public void ManaString_ManaSymbolArray_ManaCost()
        {
            var symbolArray = new [] { "3", "W", "W" };
            var manaCost = "{3}{W}{W}";

            var translation = symbolArray.Select(_cardText.ManaString);
            var parsedCost = string.Join(string.Empty, translation);

            Assert.Equal(manaCost, parsedCost);
        }


        [Fact]
        public void FindThenString_ManaCost_SameValue()
        {
            var manaCost = "{3}{W}{W}";

            var parsedMana = _cardText.FindMana(manaCost).Select(m => m.Value);
            var translation = parsedMana.Select(_cardText.ManaString);

            var parsedCost = string.Join(string.Empty, translation);

            Assert.Equal(manaCost, parsedCost);
        }


        [Theory]
        [InlineData("[+2]", "+", "2")]
        [InlineData("[−1]", "−", "1")]
        [InlineData("[0]", null, "0")]
        [InlineData("[+10]", "+", "10")]
        public void FindLoyalties_SingleLoyalty_SingleSymbol(string loyalty, string direction, string value)
        {
            var parsedSymbols = _cardText.FindLoyalties(loyalty);
            var symbol = parsedSymbols[0];

            Assert.Single(parsedSymbols);

            Assert.Equal(direction, symbol.Direction);
            Assert.Equal(value, symbol.Value);
        }


        [Theory]
        [InlineData("I —", "I")]
        [InlineData("III —", "III")]
        [InlineData("IV —", "IV")]
        public void FindSagas_SingleSaga_SingleSymbol(string saga, string value)
        {
            var parsedSymbols = _cardText.FindSagas(saga);
            var symbol = parsedSymbols[0];

            Assert.Single(parsedSymbols);

            Assert.Equal(value, symbol.Value);
            Assert.False(symbol.HasNext);
        }


        [Fact]
        public void FindSagas_MultipleSagas_MultipleSymbols()
        {
            var sagas = "I, II —";
            var parsedSymbols = _cardText.FindSagas(sagas);

            var first = parsedSymbols[0];
            var second = parsedSymbols[1];

            Assert.Equal(2, parsedSymbols.Count);

            Assert.Equal("I", first.Value);
            Assert.True(first.HasNext);

            Assert.Equal("II", second.Value);
            Assert.False(second.HasNext);
        }


        [Fact]
        public void SagaString_MultipleSymbols_MultipleSagas()
        {
            var symbols = new SagaSymbol[] 
            { 
                new(default, "I", true), new(default, "II", false)
            };

            var sagas = "I, II —";

            var sagaStrings = symbols.Select(_cardText.SagaString);
            var parsedSagas = string.Join(string.Empty, sagaStrings);

            Assert.Equal(sagas, parsedSagas);
        }
    }
}