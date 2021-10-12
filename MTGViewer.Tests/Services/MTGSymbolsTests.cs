using Xunit;
using MTGViewer.Services;

namespace MTGViewer.Tests.Services
{
    public class MTGSymbolsTests
    {
        private readonly IMTGSymbols _mtgSymbols;

        public MTGSymbolsTests(IMTGSymbols mtgSymbols)
        {
            _mtgSymbols = mtgSymbols;
        }


        [Fact]
        public void FindSymbols_ManaCost_ManaSymbolArray()
        {
            var manaCost = "{3}{W}{W}";
            var symbolArray = new [] { "3", "W", "W" };

            var parsedSymbols = _mtgSymbols.FindSymbols(manaCost);

            Assert.Equal(symbolArray, parsedSymbols);
        }


        [Fact]
        public void JoinSymbols_ManaSymbolArray_ManaCost()
        {
            var symbolArray = new [] { "3", "W", "W" };
            var manaCost = "{3}{W}{W}";

            var joinedSymbols = _mtgSymbols.JoinSymbols(symbolArray);

            Assert.Equal(manaCost, joinedSymbols);
        }


        [Fact]
        public void FindThenJoinSymbols_ManaCost_SameValue()
        {
            var manaCost = "{3}{W}{W}";

            var parsedSymbols = _mtgSymbols.FindSymbols(manaCost);
            var joinedSymbols = _mtgSymbols.JoinSymbols(parsedSymbols);

            Assert.Equal(manaCost, joinedSymbols);
        }


        [Fact]
        public void InjectSymbols_Null_EmptyString()
        {
            string nullText = null;

            var markup = _mtgSymbols.InjectIcons(nullText);

            Assert.Equal(string.Empty, markup);
        }


        [Fact]
        public void InjectSymbols_WhiteLetter_WhiteSymbolClass()
        {
            var whiteLetter = "{W}";

            var markup = _mtgSymbols.InjectIcons(whiteLetter);

            Assert.Contains("ms ms-w ms-cost", markup);
        }


        [Fact]
        public void InjectSymbols_ThreeRedLetter_ThreeRedSymbolClass()
        {
            var threeRedLetter = "{3}{R}";

            var markup = _mtgSymbols.InjectIcons(threeRedLetter);

            Assert.Contains("ms ms-3 ms-cost", markup);
            Assert.Contains("ms ms-r ms-cost", markup);
        }


        [Fact]
        public void InjectSymbols_TapLetter_TapSymbolClass()
        {
            var tapLetter = "{T}";

            var markup = _mtgSymbols.InjectIcons(tapLetter);

            Assert.Contains("ms ms-tap ms-cost", markup);
        }


        [Fact]
        public void InjectSymbols_LlanowarText_TapGreenSymbolClass()
        {
            var llanowarText = "{T}: Add {G}.";

            var markup = _mtgSymbols.InjectIcons(llanowarText);

            Assert.Contains("ms ms-tap ms-cost", markup);
            Assert.Contains(": Add ", markup);
            Assert.Contains("ms ms-g ms-cost", markup);
        }


        [Fact]
        public void InjectSymbols_LoyaltyUpText_LoyaltyUpSymbolClass()
        {
            var loyaltyUp = "[+3]";

            var markup = _mtgSymbols.InjectIcons(loyaltyUp);

            Assert.Contains("ms ms-loyalty-up ms-loyalty-3", markup);
        }


        [Fact]
        public void InjectSymbols_LoyaltyDownText_LoyaltyDownSymbolClass()
        {
            var loyaltyDown = "[−2]";

            var markup = _mtgSymbols.InjectIcons(loyaltyDown);

            Assert.Contains("ms ms-loyalty-down ms-loyalty-2", markup);
        }


        [Fact]
        public void InjectSymbols_LoyaltyZeroText_LoyaltyZeroSymbolClass()
        {
            var loyaltyZero = "[0]";

            var markup = _mtgSymbols.InjectIcons(loyaltyZero);

            Assert.Contains("ms ms-loyalty-zero ms-loyalty-0", markup);
        }


        [Fact]
        public void InjectSymbols_AjaniGreatheartedText_LoyaltySymbolClasses()
        {
            var ajaniText = "Creatures you control have vigilance.\n"
                + "[+1]: You gain 3 life.\n"
                + "[−2]: Put a +1/+1 counter on each creature you control "
                + "and a loyalty counter on each other planeswalker you control.";

            var markup = _mtgSymbols.InjectIcons(ajaniText);

            Assert.Contains("ms ms-loyalty-up ms-loyalty-1", markup);
            Assert.Contains("ms ms-loyalty-down ms-loyalty-2", markup);
        }


        [Fact]
        public void InjectSymbols_SagaOneText_SagaOneSymbolClass()
        {
            var sagaOne = "I —";

            var markup = _mtgSymbols.InjectIcons(sagaOne);

            Assert.Contains("ms ms-saga ms-saga-1", markup);
        }


        [Fact]
        public void InjectSymbols_SagaFourText_SagaFourSymbolClass()
        {
            var sagaFour = "IV —";

            var markup = _mtgSymbols.InjectIcons(sagaFour);

            Assert.Contains("ms ms-saga ms-saga-4", markup);
        }


        [Fact]
        public void InjectSymbols_SagaOneTwoThreeText_SagaOneTwoThreeSymbolClass()
        {
            var sagaOne = "I — II, III —";

            var markup = _mtgSymbols.InjectIcons(sagaOne);

            Assert.Contains("ms ms-saga ms-saga-1", markup);
            Assert.Contains("ms ms-saga ms-saga-2", markup);
            Assert.Contains("ms ms-saga ms-saga-3", markup);
        }


        [Fact]
        public void InjectSymbols_HistoryBenaliaText_SagaSymbolClasses()
        {
            var benaliaText = "(As this Saga enters and after your draw step, "
                + "add a lore counter. Sacrifice after III.)"
                + "I, II — Create a 2/2 white Knight creature token with vigilance."
                + "III — Knights you control get +2/+1 until end of turn.";

            var markup = _mtgSymbols.InjectIcons(benaliaText);

            Assert.Contains("ms ms-saga ms-saga-1", markup);
            Assert.Contains("ms ms-saga ms-saga-2", markup);
            Assert.Contains("ms ms-saga ms-saga-3", markup);
        }


        [Fact]
        public void InjectSymbols_FirstIroanText_SagaSymbolClasses()
        {
            var firstIroanText = "(As this Saga enters and after your draw step, add a "
                + "lore counter. Sacrifice after IV.)\n"
                + "I — Create a 1/1 white Human Soldier creature token.\n"
                + "II — Put three +1/+1 counters on target creature you control.\n"
                + "III — If you control a creature with power 4 or greater, draw two cards.\n"
                + "IV — Create a Gold token. (It's an artifact with "
                + "\"Sacrifice this artifact: Add one mana of any color.\")";

            var markup = _mtgSymbols.InjectIcons(firstIroanText);

            Assert.Contains("ms ms-saga ms-saga-1", markup);
            Assert.Contains("ms ms-saga ms-saga-2", markup);
            Assert.Contains("ms ms-saga ms-saga-3", markup);
            Assert.Contains("ms ms-saga ms-saga-4", markup);
        }
    }
}