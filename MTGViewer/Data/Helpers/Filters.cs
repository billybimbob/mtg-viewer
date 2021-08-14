using System;
using System.Linq.Expressions;

namespace MTGViewer.Data
{
    // TODO: figure out better way to create expressions
    public static class SuggestFilter
    {
        public static Expression<Func<Suggestion, bool>> WaitingFor(string userId) =>
            suggest => suggest.ReceiverId == userId;


        public static Expression<Func<Suggestion, bool>> Involves(string userId) =>
            suggest => suggest.ProposerId == userId 
                || suggest.ReceiverId == userId;


        public static Expression<Func<Suggestion, bool>> Involves(string userId, int deckId) =>
            suggest => (suggest.ProposerId == userId || suggest.ReceiverId == userId)
                && suggest.ToId == deckId;
    }


    public static class TradeFilter
    {
        public static Expression<Func<Trade, bool>> WaitingFor(string userId) =>
            trade => trade.ReceiverId == userId && !trade.IsCounter
                || trade.ProposerId == userId && trade.IsCounter;


        public static Expression<Func<Trade, bool>> Involves(string userId) =>
            trade => trade.ProposerId == userId 
                || trade.ReceiverId == userId;


        public static Expression<Func<Trade, bool>> Involves(string userId, int deckId) =>
            trade => (trade.ProposerId == userId || trade.ReceiverId == userId)
                && (trade.ToId == deckId || trade.FromId == deckId);
    }
}