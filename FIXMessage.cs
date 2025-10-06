using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Simple FIX message representation for simulator
    /// </summary>
    public class FIXMessage
    {
        public Dictionary<string, string> Fields { get; set; }
        public DateTime Timestamp { get; set; }

        public FIXMessage(string msgType)
        {
            Fields = new Dictionary<string, string>
            {
                ["35"] = msgType
            };
            Timestamp = DateTime.UtcNow;
        }

        public FIXMessage Set(string tag, string value)
        {
            Fields[tag] = value;
            return this;
        }

        public string Get(string tag, string defaultValue = null)
        {
            return Fields.TryGetValue(tag, out var value) ? value : defaultValue;
        }

        public string MsgType => Get("35");

        public string ToFixString()
        {
            // Sort by tag number for proper FIX ordering
            // Skip non-numeric tags (like leg1_xxx which are just for demo)
            var ordered = Fields
                .Where(kvp => int.TryParse(kvp.Key, out _)) // Only include numeric tags
                .OrderBy(kvp => int.Parse(kvp.Key))
                .Select(kvp => $"{kvp.Key}={kvp.Value}");

            return string.Join("|", ordered);
        }

        public override string ToString()
        {
            return $"FIX({MsgType}): {ToFixString()}";
        }
    }

    /// <summary>
    /// FIX message types
    /// </summary>
    public static class MsgTypes
    {
        public const string QuoteRequest = "R";
        public const string Quote = "S";
        public const string QuoteStatusReport = "AI";
        public const string QuoteCancel = "Z";
        public const string NewOrderMultileg = "AB";
        public const string ExecutionReport = "8";
        public const string BusinessMessageReject = "j";
    }

    /// <summary>
    /// Common FIX tags
    /// </summary>
    public static class Tags
    {
        // Header
        public const string MsgType = "35";
        public const string SenderCompID = "49";
        public const string TargetCompID = "56";
        public const string OnBehalfOfCompID = "115";
        public const string DeliverToCompID = "128";
        public const string SendingTime = "52";

        // Identification
        public const string QuoteReqID = "131";
        public const string QuoteID = "117";
        public const string ClOrdID = "11";
        public const string OrderID = "37";

        // Product
        public const string Symbol = "55";
        public const string Structure = "9126";
        public const string NoLegs = "555";
        public const string LegSymbol = "600";
        public const string LegStrikePrice = "612";
        public const string LegQty = "687";
        public const string LegCurrency = "556";
        public const string LegStrategyInd = "6714";
        public const string LegStrategyID = "7940";

        // Dates
        public const string Tenor = "6215";
        public const string LegMaturityDate = "611";
        public const string DeliveryDate = "743";
        public const string PremiumDelivery = "5020";
        public const string Cutoff = "9125";

        // Pricing
        public const string Side = "54";
        public const string Volatility = "5678";
        public const string LegPremPrice = "5844";
        public const string MQSize = "5359";
        public const string LegSpotRate = "5235";
        public const string LegForwardPoints = "5191";
        public const string DepoRate = "9115";
        public const string DepoRateCcy = "9073";
        public const string LegDelta = "6035";
        public const string MQStrikePrice = "6354";

        // Status
        public const string QuoteStatus = "297";
        public const string OrdStatus = "39";
        public const string ExecType = "150";

        // Custom Fenics
        public const string NoBanksReqFenics = "8051";
        public const string PremiumCcy = "5830";
        public const string HedgeTradeType = "9016";
        public const string FXOptionStyle = "9019";
        public const string Position = "6351";
        public const string PriceIndicator = "9904";
    }

    /// <summary>
    /// FIX field values
    /// </summary>
    public static class Values
    {
        public static class Side
        {
            public const string Buy = "1";
            public const string Sell = "2";
        }

        public static class OrdStatus
        {
            public const string New = "0";
            public const string PartiallyFilled = "1";
            public const string Filled = "2";
            public const string Canceled = "4";
            public const string Rejected = "8";
        }

        public static class Structure
        {
            public const string Call = "1";
            public const string Put = "2";
            public const string CallSpread = "8";
            public const string PutSpread = "9";
            public const string RiskReversal = "5";
        }
    }
}