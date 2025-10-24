using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using QuickFix;
using QuickFix.Fields;

namespace FXOptionsSimulator.FIX
{
    public class GFIFIXApplication : MessageCracker, IApplication
    {
        private readonly ConcurrentDictionary<string, StreamInfo> _quotes;

        public class StreamInfo
        {
            public string LP { get; set; }
            public string QuoteReqID { get; set; }
            public string GroupID { get; set; }
            public FIXMessage BidQuote { get; set; }
            public FIXMessage OfferQuote { get; set; }
            public DateTime LastUpdate { get; set; }
        }

        public event Action<string> OnLogonEvent;
        public event Action<string> OnLogoutEvent;
        public event Action<string, FIXMessage> OnQuoteReceived;

        public bool IsLoggedOn { get; private set; }

        public GFIFIXApplication()
        {
            _quotes = new ConcurrentDictionary<string, StreamInfo>();
            Console.WriteLine("[GFI FIX App] Initialized");
        }

        #region IApplication Implementation

        public void OnCreate(SessionID sessionID)
        {
            Console.WriteLine($"[GFI FIX] Session created: {sessionID}");
        }

        public void OnLogon(SessionID sessionID)
        {
            IsLoggedOn = true;
            Console.WriteLine($"[GFI FIX] ✓✓✓ LOGGED ON ✓✓✓");
            OnLogonEvent?.Invoke(sessionID.ToString());
        }

        public void OnLogout(SessionID sessionID)
        {
            IsLoggedOn = false;
            Console.WriteLine($"[GFI FIX] ✗ LOGGED OUT");
            OnLogoutEvent?.Invoke(sessionID.ToString());
        }

        public void ToAdmin(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);

            if (msgType == QuickFix.Fields.MsgType.LOGON)
            {
                message.SetField(new Username("swed.obo.stg.api"));
                message.SetField(new Password("ZQcZokEOLjb9"));

                Console.WriteLine("[GFI FIX] >>> Sending Logon with credentials");

                // ADD THESE DEBUG LINES:
                Console.WriteLine($"[DEBUG] Full Logon Message:");
                Console.WriteLine($"{message.ToString()}");
                Console.WriteLine($"[DEBUG] End Logon Message");
            }
        }

        public void FromAdmin(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);
            Console.WriteLine($"[GFI FIX] <<< Admin: {msgType}");

            // If it's a reject, parse it
            if (msgType == "3")
            {
                ParseRejectMessage(message);
            }
        }

        public void ToApp(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);
            Console.WriteLine($"[GFI FIX] >>> Sending: {msgType}");

            // ADD THIS - Print full message for Quote Requests
            if (msgType == "R")
            {
                Console.WriteLine($"\n[DEBUG] Full Quote Request Message:");
                Console.WriteLine($"{message.ToString()}");
                Console.WriteLine($"[DEBUG] End of message\n");
            }
        }

        public void FromApp(QuickFix.Message message, SessionID sessionID)
        {
            Crack(message, sessionID);
        }

        #endregion

        #region Message Handlers

        /// <summary>
        /// Handle Quote messages (35=S) - THIS IS WHERE PREMIUMS ARRIVE
        /// </summary>
        public void OnMessage(QuickFix.FIX44.Quote quote, SessionID sessionID)
        {
            try
            {
                string lpName = quote.GetString(Tags.OnBehalfOfCompID);
                string quoteReqID = quote.GetString(Tags.QuoteReqID);
                string quoteID = quote.GetString(Tags.QuoteID);
                string sideStr = quote.GetString(Tags.Side);
                string side = sideStr == "1" ? "BID" : "OFFER";

                Console.WriteLine($"\n[GFI FIX] <<< REAL QUOTE (35=S)");
                Console.WriteLine($"  LP: {lpName}");
                Console.WriteLine($"  QuoteReqID: {quoteReqID}");
                Console.WriteLine($"  Side: {side}");

                var fixMsg = ConvertQuoteToFIXMessage(quote);

                string key = $"{quoteReqID}_{lpName}";

                _quotes.AddOrUpdate(key,
                    new StreamInfo
                    {
                        LP = lpName,
                        QuoteReqID = quoteReqID,
                        GroupID = "default",
                        BidQuote = side == "BID" ? fixMsg : null,
                        OfferQuote = side == "OFFER" ? fixMsg : null,
                        LastUpdate = DateTime.UtcNow
                    },
                    (k, existing) =>
                    {
                        if (side == "BID")
                            existing.BidQuote = fixMsg;
                        else
                            existing.OfferQuote = fixMsg;
                        existing.LastUpdate = DateTime.UtcNow;
                        return existing;
                    });

                OnQuoteReceived?.Invoke(quoteReqID, fixMsg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GFI FIX] ERROR: {ex.Message}");
                Console.WriteLine($"[GFI FIX] Stack: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Handle Quote Status Report (35=AI)
        /// </summary>
        public void OnMessage(QuickFix.FIX44.QuoteStatusReport report, SessionID sessionID)
        {
            string quoteReqID = report.GetString(Tags.QuoteReqID);
            int quoteStatus = report.IsSetField(Tags.QuoteStatus) ? report.GetInt(Tags.QuoteStatus) : -1;

            Console.WriteLine($"\n[GFI FIX] <<< Quote Status Report (35=AI)");
            Console.WriteLine($"  QuoteReqID: {quoteReqID}");
            Console.WriteLine($"  QuoteStatus: {quoteStatus} ({GetQuoteStatusText(quoteStatus)})");

            if (report.IsSetField(58)) // Text
            {
                string text = report.GetString(58);
                Console.WriteLine($"  Text: {text}");
            }
        }

        /// <summary>
        /// Handle Quote Request Reject (35=AG)
        /// </summary>
        public void OnMessage(QuickFix.FIX44.QuoteRequestReject reject, SessionID sessionID)
        {
            Console.WriteLine($"\n[GFI FIX] <<< QUOTE REQUEST REJECT (35=AG)");
            Console.WriteLine(new string('=', 60));

            if (reject.IsSetField(131)) // QuoteReqID
            {
                string quoteReqID = reject.GetString(131);
                Console.WriteLine($"  QuoteReqID: {quoteReqID}");
            }

            if (reject.IsSetField(658)) // QuoteRequestRejectReason
            {
                int rejectReason = reject.GetInt(658);
                Console.WriteLine($"  RejectReason: {rejectReason} ({GetQuoteRequestRejectReasonText(rejectReason)})");
            }

            if (reject.IsSetField(58)) // Text
            {
                string rejectText = reject.GetString(58);
                Console.WriteLine($"  ⚠️  Reject Text: {rejectText}");
            }

            Console.WriteLine(new string('=', 60));
        }

        /// <summary>
        /// Handle Session Reject (35=3)
        /// </summary>
        public void OnMessage(QuickFix.FIX44.Reject reject, SessionID sessionID)
        {
            Console.WriteLine($"\n[GFI FIX] <<< SESSION REJECT (35=3)");
            Console.WriteLine(new string('=', 60));

            if (reject.IsSetRefSeqNum())
            {
                int refSeqNum = reject.RefSeqNum.getValue();
                Console.WriteLine($"  RefSeqNum: {refSeqNum}");
            }

            if (reject.IsSetRefTagID())
            {
                int refTagID = reject.RefTagID.getValue();
                Console.WriteLine($"  RefTagID: {refTagID} (Field that caused rejection)");
            }

            if (reject.IsSetRefMsgType())
            {
                string refMsgType = reject.RefMsgType.getValue();
                Console.WriteLine($"  RefMsgType: {refMsgType}");
            }

            if (reject.IsSetSessionRejectReason())
            {
                int rejectReason = reject.SessionRejectReason.getValue();
                Console.WriteLine($"  SessionRejectReason: {rejectReason} ({GetSessionRejectReasonText(rejectReason)})");
            }

            if (reject.IsSetText())
            {
                string rejectText = reject.Text.getValue();
                Console.WriteLine($"  ⚠️  Reject Text: {rejectText}");
            }

            Console.WriteLine(new string('=', 60));
        }

        /// <summary>
        /// Handle Execution Report (35=8) - Trade fill/reject
        /// </summary>
        public void OnMessage(QuickFix.FIX44.ExecutionReport execReport, SessionID sessionID)
        {
            string clOrdID = execReport.GetString(Tags.ClOrdID);
            string ordStatus = execReport.GetString(Tags.OrdStatus);

            Console.WriteLine($"\n[GFI FIX] <<< Execution Report (35=8)");
            Console.WriteLine($"  ClOrdID: {clOrdID}");
            Console.WriteLine($"  Status: {ordStatus}");

            if (ordStatus == "2")
            {
                Console.WriteLine("  ✓ FILLED!");
            }
            else if (ordStatus == "8")
            {
                Console.WriteLine("  ✗ REJECTED");

                if (execReport.IsSetField(58)) // Text
                {
                    string text = execReport.GetString(58);
                    Console.WriteLine($"  Reason: {text}");
                }
            }
        }

        /// <summary>
        /// Handle Business Message Reject (35=j)
        /// </summary>
        public void OnMessage(QuickFix.FIX44.BusinessMessageReject reject, SessionID sessionID)
        {
            Console.WriteLine($"\n[GFI FIX] <<< BUSINESS MESSAGE REJECT (35=j)");
            Console.WriteLine(new string('=', 60));

            if (reject.IsSetField(372)) // RefMsgType
            {
                string refMsgType = reject.GetString(372);
                Console.WriteLine($"  RefMsgType: {refMsgType}");
            }

            if (reject.IsSetField(380)) // BusinessRejectReason
            {
                int rejectReason = reject.GetInt(380);
                Console.WriteLine($"  BusinessRejectReason: {rejectReason} ({GetBusinessRejectReasonText(rejectReason)})");
            }

            if (reject.IsSetField(58)) // Text
            {
                string rejectText = reject.GetString(58);
                Console.WriteLine($"  ⚠️  Reject Text: {rejectText}");
            }

            Console.WriteLine(new string('=', 60));
        }

        /// <summary>
        /// Handle StaticData message (35=SD) - GFI custom message
        /// </summary>
        public void OnMessage(QuickFix.Message message, SessionID sessionID)
        {
            try
            {
                var msgType = message.Header.GetField(Tags.MsgType);

                if (msgType == "SD") // StaticData
                {
                    Console.WriteLine($"\n[GFI FIX] <<< StaticData (35=SD) - LP Information");
                    Console.WriteLine(new string('-', 60));

                    if (message.IsSetField(1663)) // NumElements
                    {
                        int numElements = message.GetInt(1663);
                        Console.WriteLine($"  Available Liquidity Providers: {numElements}\n");

                        for (int i = 1; i <= numElements; i++)
                        {
                            try
                            {
                                var group = message.GetGroup(i, 1663);

                                string lpCompID = group.IsSetField(Tags.OnBehalfOfCompID)
                                    ? group.GetString(Tags.OnBehalfOfCompID)
                                    : "N/A";

                                string displayName = group.IsSetField(1402) // DisplayName
                                    ? group.GetString(1402)
                                    : "N/A";

                                string priceRequest = group.IsSetField(9996) // PriceRequest
                                    ? group.GetString(9996)
                                    : "N/A";

                                Console.WriteLine($"  [{i,2}] {lpCompID,-15} | {displayName}");
                                Console.WriteLine($"      PriceRequest: {priceRequest}");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"  [{i,2}] Error parsing LP data: {ex.Message}");
                            }
                        }

                        Console.WriteLine(new string('-', 60));
                        Console.WriteLine("  ✓ StaticData received successfully");
                    }
                }
                else
                {
                    Console.WriteLine($"[GFI FIX] <<< Custom message type: {msgType}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GFI FIX] ERROR handling custom message: {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private void ParseRejectMessage(QuickFix.Message message)
        {
            try
            {
                Console.WriteLine($"\n[GFI FIX] === PARSING REJECT MESSAGE ===");

                if (message.IsSetField(45)) // RefSeqNum
                {
                    int refSeqNum = message.GetInt(45);
                    Console.WriteLine($"  RefSeqNum: {refSeqNum}");
                }

                if (message.IsSetField(371)) // RefTagID
                {
                    int refTagID = message.GetInt(371);
                    Console.WriteLine($"  RefTagID: {refTagID}");
                }

                if (message.IsSetField(372)) // RefMsgType
                {
                    string refMsgType = message.GetString(372);
                    Console.WriteLine($"  RefMsgType: {refMsgType}");
                }

                if (message.IsSetField(373)) // SessionRejectReason
                {
                    int reason = message.GetInt(373);
                    Console.WriteLine($"  SessionRejectReason: {reason} ({GetSessionRejectReasonText(reason)})");
                }

                if (message.IsSetField(58)) // Text
                {
                    string text = message.GetString(58);
                    Console.WriteLine($"  ⚠️  Text: {text}");
                }

                Console.WriteLine($"=================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error parsing reject: {ex.Message}");
            }
        }

        private FIXMessage ConvertQuoteToFIXMessage(QuickFix.FIX44.Quote quote)
        {
            var msg = new FIXMessage(MsgTypes.Quote);

            msg.Set(Tags.OnBehalfOfCompID.ToString(), quote.GetString(Tags.OnBehalfOfCompID));
            msg.Set(Tags.QuoteReqID.ToString(), quote.GetString(Tags.QuoteReqID));
            msg.Set(Tags.QuoteID.ToString(), quote.GetString(Tags.QuoteID));
            msg.Set(Tags.Side.ToString(), quote.GetString(Tags.Side));
            msg.Set(Tags.Symbol.ToString(), quote.GetString(Tags.Symbol));

            if (quote.IsSetField(Tags.NoLegs))
            {
                int noLegs = quote.GetInt(Tags.NoLegs);
                msg.Set(Tags.NoLegs.ToString(), noLegs.ToString());

                Console.WriteLine($"  Parsing {noLegs} legs:");

                for (int i = 1; i <= noLegs; i++)
                {
                    var legGroup = quote.GetGroup(i, Tags.NoLegs);

                    if (legGroup.IsSetField(5844)) // LegPremPrice
                    {
                        string legPrem = legGroup.GetString(5844);
                        msg.Set($"leg{i}_5844", legPrem);
                        Console.WriteLine($"    Leg {i} Premium: {legPrem}");
                    }

                    if (legGroup.IsSetField(5678)) // Volatility
                    {
                        string vol = legGroup.GetString(5678);
                        msg.Set($"leg{i}_5678", vol);
                        Console.WriteLine($"    Leg {i} Vol: {vol}");
                    }
                }
            }

            return msg;
        }

        public List<StreamInfo> GetActiveStreams(string groupId)
        {
            var result = new List<StreamInfo>();

            foreach (var stream in _quotes.Values)
            {
                result.Add(stream);
            }

            return result;
        }

        private string GetQuoteStatusText(int status)
        {
            return status switch
            {
                0 => "Accepted",
                1 => "Canceled for Symbol",
                2 => "Canceled for Security Type",
                3 => "Canceled for Underlying",
                4 => "Canceled All",
                5 => "Rejected",
                6 => "Removed from Market",
                7 => "Expired",
                8 => "Query",
                9 => "Quote Not Found",
                10 => "Pending",
                11 => "Pass",
                _ => $"Unknown ({status})"
            };
        }

        private string GetQuoteRequestRejectReasonText(int reason)
        {
            return reason switch
            {
                1 => "Unknown Symbol",
                2 => "Exchange Closed",
                3 => "Quote Request Exceeds Limit",
                4 => "Too Late to Enter",
                5 => "Unknown Quote",
                6 => "Duplicate Quote",
                7 => "Invalid Bid/Ask Spread",
                8 => "Invalid Price",
                9 => "Not Authorized to Quote",
                99 => "Other",
                _ => $"Unknown ({reason})"
            };
        }

        private string GetSessionRejectReasonText(int reason)
        {
            return reason switch
            {
                0 => "Invalid tag number",
                1 => "Required tag missing",
                2 => "Tag not defined for this message type",
                3 => "Undefined Tag",
                4 => "Tag specified without a value",
                5 => "Value is incorrect (out of range) for this tag",
                6 => "Incorrect data format for value",
                7 => "Decryption problem",
                8 => "Signature problem",
                9 => "CompID problem",
                10 => "SendingTime accuracy problem",
                11 => "Invalid MsgType",
                12 => "XML Validation error",
                13 => "Tag appears more than once",
                14 => "Tag specified out of required order",
                15 => "Repeating group fields out of order",
                16 => "Incorrect NumInGroup count for repeating group",
                17 => "Non 'data' value includes field delimiter",
                99 => "Other",
                _ => $"Unknown ({reason})"
            };
        }

        private string GetBusinessRejectReasonText(int reason)
        {
            return reason switch
            {
                0 => "Other",
                1 => "Unknown ID",
                2 => "Unknown Security",
                3 => "Unsupported Message Type",
                4 => "Application not available",
                5 => "Conditionally Required Field Missing",
                6 => "Not Authorized",
                7 => "DeliverTo firm not available at this time",
                _ => $"Unknown ({reason})"
            };
        }

        #endregion
    }
}