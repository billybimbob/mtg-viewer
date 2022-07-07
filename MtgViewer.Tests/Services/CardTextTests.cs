using System.Linq;

using Xunit;

using MtgViewer.Services.Symbols;

namespace MtgViewer.Tests.Services;

public class CardTextTests
{
    private const string LongDash = "\u2212";
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
        const string manaCost = "{3}{W}{W}";

        string[] manaSymbols = { "3", "W", "W" };

        var parsedMana = _cardText.FindMana(manaCost).Select(m => m.Value);

        Assert.Equal(manaSymbols, parsedMana);
    }

    [Fact]
    public void ManaString_ManaSymbolArray_ManaCost()
    {
        const string manaCost = "{3}{W}{W}";

        string[] symbolArray = { "3", "W", "W" };

        string translation = symbolArray
            .Select(_cardText.ManaString)
            .Join();

        Assert.Equal(manaCost, translation);
    }

    [Fact]
    public void FindThenString_ManaCost_SameValue()
    {
        const string manaCost = "{3}{W}{W}";

        var parsedMana = _cardText.FindMana(manaCost).Select(m => m.Value);

        string translation = _cardText
            .FindMana(manaCost)
            .Select(m => _cardText.ManaString(m.Value))
            .Join();

        Assert.Equal(manaCost, translation);
    }

    [Theory]
    [InlineData("[+2]", "+", "2")]
    [InlineData($"[{LongDash}1]", LongDash, "1")]
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
        const string sagas = "I, II —";
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
        var symbols = new[]
        {
            new SagaSymbol(default, "I", true), new SagaSymbol(default, "II", false)
        };

        const string sagas = "I, II —";

        string parsedSagas = symbols
            .Select(_cardText.SagaString)
            .Join();

        Assert.Equal(sagas, parsedSagas);
    }
}
