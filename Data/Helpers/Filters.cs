using System;
using System.Linq.Expressions;

namespace MTGViewer.Data
{
    public static class TradeFilter
    {
        public static Expression<Func<Trade, bool>> WaitingFor(string userId) =>
            trade =>
                trade.FromId != default
                    && (trade.ReceiverId == userId && !trade.IsCounter
                        || trade.ProposerId == userId && trade.IsCounter);

        public static Expression<Func<Trade, bool>> Involves(string userId) =>
            trade =>
                trade.FromId != default
                    && (trade.ReceiverId == userId || trade.ProposerId == userId);

        public static Expression<Func<Trade, bool>> Involves(int deckId) =>
            trade =>
                trade.FromId != default
                    && (trade.ToId == deckId || trade.FromId == deckId);

        public static Expression<Func<Trade, bool>> Suggestion =>
            trade => trade.FromId == default;

        public static Expression<Func<Trade, bool>> SuggestionFor(string userId) =>
            trade =>
                trade.FromId == default
                    && trade.ReceiverId == userId;
    }


    public static class LocationFilter
    {
        public static Expression<Func<Location, bool>> IsShared =>
            location => location.OwnerId == default;
    }
}