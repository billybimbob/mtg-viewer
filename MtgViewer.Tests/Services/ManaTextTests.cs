using Xunit;

using MtgViewer.Services.Symbols;

namespace MtgViewer.Tests.Services;

public class ManaTextTests
{
    private readonly SymbolFormatter _manaText;

    public ManaTextTests(CardText cardText, ManaTranslator manaTranslator)
    {
        _manaText = cardText.ComposeWith(manaTranslator);
    }

    [Fact]
    public void Format_Null_EmptyString()
    {
        const string? nullText = null;

        string markup = _manaText.Format(nullText);

        Assert.Equal(string.Empty, markup);
    }

    [Fact]
    public void Format_WhiteLetter_WhiteSymbolClass()
    {
        const string whiteLetter = "{W}";

        string markup = _manaText.Format(whiteLetter);

        Assert.Contains("ms ms-w ms-cost", markup);
    }

    [Fact]
    public void Format_ThreeRedLetter_ThreeRedSymbolClass()
    {
        const string threeRedLetter = "{3}{R}";

        string markup = _manaText.Format(threeRedLetter);

        Assert.Contains("ms ms-3 ms-cost", markup);
        Assert.Contains("ms ms-r ms-cost", markup);
    }

    [Fact]
    public void Format_TapLetter_TapSymbolClass()
    {
        const string tapLetter = "{T}";

        string markup = _manaText.Format(tapLetter);

        Assert.Contains("ms ms-tap ms-cost", markup);
    }

    [Fact]
    public void Format_LlanowarText_TapGreenSymbolClass()
    {
        const string llanowarText = "{T}: Add {G}.";

        string markup = _manaText.Format(llanowarText);

        Assert.Contains("ms ms-tap ms-cost", markup);
        Assert.Contains(": Add ", markup);
        Assert.Contains("ms ms-g ms-cost", markup);
    }

    [Fact]
    public void Format_LoyaltyUpText_LoyaltyUpSymbolClass()
    {
        const string loyaltyUp = "[+3]";

        string markup = _manaText.Format(loyaltyUp);

        Assert.Contains("ms ms-loyalty-up ms-loyalty-3", markup);
    }

    [Fact]
    public void Format_LoyaltyDownText_LoyaltyDownSymbolClass()
    {
        const string loyaltyDown = $"[{CardText.Minus}2]";

        string markup = _manaText.Format(loyaltyDown);

        Assert.Contains("ms ms-loyalty-down ms-loyalty-2", markup);
    }

    [Fact]
    public void Format_LoyaltyZeroText_LoyaltyZeroSymbolClass()
    {
        const string loyaltyZero = "[0]";

        string markup = _manaText.Format(loyaltyZero);

        Assert.Contains("ms ms-loyalty-zero ms-loyalty-0", markup);
    }

    [Fact]
    public void Format_AjaniGreatheartedText_LoyaltySymbolClasses()
    {
        const string ajaniText = "Creatures you control have vigilance.\n"
            + "[+1]: You gain 3 life.\n"
            + $"[{CardText.Minus}2]: Put a +1/+1 counter on each creature you control "
            + "and a loyalty counter on each other planeswalker you control.";

        string markup = _manaText.Format(ajaniText);

        Assert.Contains("ms ms-loyalty-up ms-loyalty-1", markup);
        Assert.Contains("ms ms-loyalty-down ms-loyalty-2", markup);
    }

    [Fact]
    public void Format_SagaOneText_SagaOneSymbolClass()
    {
        const string sagaOne = "I —";

        string markup = _manaText.Format(sagaOne);

        Assert.Contains("ms ms-saga ms-saga-1", markup);
    }

    [Fact]
    public void Format_SagaFourText_SagaFourSymbolClass()
    {
        const string sagaFour = "IV —";

        string markup = _manaText.Format(sagaFour);

        Assert.Contains("ms ms-saga ms-saga-4", markup);
    }

    [Fact]
    public void Format_SagaOneTwoThreeText_SagaOneTwoThreeSymbolClass()
    {
        const string sagaOne = "I — II, III —";

        string markup = _manaText.Format(sagaOne);

        Assert.Contains("ms ms-saga ms-saga-1", markup);
        Assert.Contains("ms ms-saga ms-saga-2", markup);
        Assert.Contains("ms ms-saga ms-saga-3", markup);
    }

    [Fact]
    public void Format_HistoryBenaliaText_SagaSymbolClasses()
    {
        const string benaliaText = "(As this Saga enters and after your draw step, "
            + "add a lore counter. Sacrifice after III.)"
            + "I, II — Create a 2/2 white Knight creature token with vigilance."
            + "III — Knights you control get +2/+1 until end of turn.";

        string markup = _manaText.Format(benaliaText);

        Assert.Contains("ms ms-saga ms-saga-1", markup);
        Assert.Contains("ms ms-saga ms-saga-2", markup);
        Assert.Contains("ms ms-saga ms-saga-3", markup);
    }

    [Fact]
    public void Format_FirstIroanText_SagaSymbolClasses()
    {
        const string firstIroanText = "(As this Saga enters and after your draw step, add a "
            + "lore counter. Sacrifice after IV.)\n"
            + "I — Create a 1/1 white Human Soldier creature token.\n"
            + "II — Put three +1/+1 counters on target creature you control.\n"
            + "III — If you control a creature with power 4 or greater, draw two cards.\n"
            + "IV — Create a Gold token. (It's an artifact with "
            + "\"Sacrifice this artifact: Add one mana of any color.\")";

        string markup = _manaText.Format(firstIroanText);

        Assert.Contains("ms ms-saga ms-saga-1", markup);
        Assert.Contains("ms ms-saga ms-saga-2", markup);
        Assert.Contains("ms ms-saga ms-saga-3", markup);
        Assert.Contains("ms ms-saga ms-saga-4", markup);
    }
}
