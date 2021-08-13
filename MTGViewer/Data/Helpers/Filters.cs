using System;
using System.Linq.Expressions;

namespace MTGViewer.Data
{
    public static class TradeFilter
    {
        public static Expression<Func<Trade, bool>> Suggestion =>
            trade => trade.FromId == default;

        public static Expression<Func<Trade, bool>> NotSuggestion =>
            trade => trade.FromId != default;


        public static Expression<Func<Trade, bool>> WaitingFor(string userId) =>
            trade => trade.ReceiverId == userId && !trade.IsCounter
                || trade.ProposerId == userId && trade.IsCounter;


        public static Expression<Func<Trade, bool>> Involves(string userId) =>
            trade => trade.ProposerId == userId 
                || trade.ReceiverId == userId;


        public static Expression<Func<Trade, bool>> Involves(string userId, int deckId) =>
            trade => (trade.ProposerId == userId || trade.ReceiverId == userId)
                && (trade.ToId == deckId || trade.From.LocationId == deckId);
    }


    public static class LocationFilter
    {
        public static Expression<Func<Location, bool>> IsShared =>
            location => location.OwnerId == default;
    }
}