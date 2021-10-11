using Xunit;
using MTGViewer.Services;

namespace MTGViewer.Tests.Services
{
    public class IconMarkupTests
    {
        private readonly IconMarkup _iconMarkup;

        public IconMarkupTests(IconMarkup iconMarkup)
        {
            _iconMarkup = iconMarkup;
        }


        [Fact]
        public void InjectSymbols_WhiteLetter_WhiteSymbolClass()
        {
            var whiteLetter = "{W}";

            var markup = _iconMarkup.InjectSymbols(whiteLetter);

            Assert.Contains("ms ms-w ms-cost", markup);
        }


        [Fact]
        public void InjectSymbols_ThreeRedLetter_ThreeRedSymbolClass()
        {
            var threeRedLetter = "{3}{R}";

            var markup = _iconMarkup.InjectSymbols(threeRedLetter);

            Assert.Contains("ms ms-3 ms-cost", markup);
            Assert.Contains("ms ms-r ms-cost", markup);
        }


        [Fact]
        public void InjectSymbols_TapLetter_TapSymbolClass()
        {
            var tapLetter = "{T}";

            var markup = _iconMarkup.InjectSymbols(tapLetter);

            Assert.Contains("ms ms-tap ms-cost", markup);
        }


        [Fact]
        public void InjectSymbols_LlanowarText_TapGreenSymbolClass()
        {
            var llanowarText = "{T}: Add {G}.";

            var markup = _iconMarkup.InjectSymbols(llanowarText);

            Assert.Contains("ms ms-tap ms-cost", markup);
            Assert.Contains(": Add ", markup);
            Assert.Contains("ms ms-g ms-cost", markup);
        }


        [Fact]
        public void InjectSymbols_LoyaltyUpText_LoyaltyUpSymbolClass()
        {
            var loyaltyUp = "[+3]";

            var markup = _iconMarkup.InjectSymbols(loyaltyUp);

            Assert.Contains("ms ms-loyalty-up ms-loyalty-3", markup);
        }


        [Fact]
        public void InjectSymbols_LoyaltyDownText_LoyaltyDownSymbolClass()
        {
            var loyaltyDown = "[−2]";

            var markup = _iconMarkup.InjectSymbols(loyaltyDown);

            Assert.Contains("ms ms-loyalty-down ms-loyalty-2", markup);
        }


        [Fact]
        public void InjectSymbols_LoyaltyZeroText_LoyaltyZeroSymbolClass()
        {
            var loyaltyZero = "[0]";

            var markup = _iconMarkup.InjectSymbols(loyaltyZero);

            Assert.Contains("ms ms-loyalty-zero ms-loyalty-0", markup);
        }


        [Fact]
        public void InjectSymbols_AjaniGreatheartedText_LoyaltySymbolClasses()
        {
            var ajaniText = "Creatures you control have vigilance.\n"
                + "[+1]: You gain 3 life.\n"
                + "[−2]: Put a +1/+1 counter on each creature you control "
                + "and a loyalty counter on each other planeswalker you control.";

            var markup = _iconMarkup.InjectSymbols(ajaniText);

            Assert.Contains("ms ms-loyalty-up ms-loyalty-1", markup);
            Assert.Contains("ms ms-loyalty-down ms-loyalty-2", markup);
        }


        [Fact]
        public void InjectSymbols_SagaOneText_SagaOneSymbolClass()
        {
            var sagaOne = "I —";

            var markup = _iconMarkup.InjectSymbols(sagaOne);

            Assert.Contains("ms ms-saga ms-saga-1", markup);
        }


        [Fact]
        public void InjectSymbols_SagaFourText_SagaFourSymbolClass()
        {
            var sagaFour = "IV —";

            var markup = _iconMarkup.InjectSymbols(sagaFour);

            Assert.Contains("ms ms-saga ms-saga-4", markup);
        }


        [Fact]
        public void InjectSymbols_SagaOneTwoThreeText_SagaOneTwoThreeSymbolClass()
        {
            var sagaOne = "I — II, III —";

            var markup = _iconMarkup.InjectSymbols(sagaOne);

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

            var markup = _iconMarkup.InjectSymbols(benaliaText);

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

            var markup = _iconMarkup.InjectSymbols(firstIroanText);

            Assert.Contains("ms ms-saga ms-saga-1", markup);
            Assert.Contains("ms ms-saga ms-saga-2", markup);
            Assert.Contains("ms ms-saga ms-saga-3", markup);
            Assert.Contains("ms ms-saga ms-saga-4", markup);
        }
    }
}