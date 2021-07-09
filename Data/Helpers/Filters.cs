using System;
using System.Linq.Expressions;

namespace MTGViewer.Data
{
    public static class TradeFilter
    {
        public static Expression<Func<Trade, bool>> PendingFor(string userId) =>
            trade =>
                trade.FromId != default
                    && (trade.ToUser.Id == userId && !trade.IsCounter
                        || trade.FromUser.Id == userId && trade.IsCounter);

        public static Expression<Func<Trade, bool>> PendingFor(int deckId) =>
            trade =>
                trade.ToId == deckId && !trade.IsCounter
                    || trade.FromId == deckId && trade.IsCounter;

        public static Expression<Func<Trade, bool>> Suggestion =>
            trade =>
                trade.FromId == default;

        public static Expression<Func<Trade, bool>> SuggestionFor(string userId) =>
            trade =>
                trade.From == default
                    && trade.ToUser.Id ==userId;
    }
}