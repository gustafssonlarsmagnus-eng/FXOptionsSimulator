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
        private ConcurrentDictionary<string, string> _quoteReqToGroupID = new ConcurrentDictionary<string, string>();
        public event Action<string, string, string> OnQuoteCanceled;

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

                Console.WriteLine($"[DEBUG] Full Logon Message:");
                Console.WriteLine($"{message.ToString()}");
                Console.WriteLine($"[DEBUG] End Logon Message");
            }
        }

        public void FromAdmin(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);
            Console.WriteLine($"[GFI FIX] <<< Admin: {msgType}");

            if (msgType == "3")
            {
                ParseRejectMessage(message);
            }
        }

        public void ToApp(QuickFix.Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetField(Tags.MsgType);
            Console.WriteLine($"[GFI FIX] >>> Sending: {msgType}");

            if (msgType == "R")
            {
                Console.WriteLine($"\n[DEBUG] Full Quote Request Message:");
                Console.WriteLine($"{message.ToString()}");
                Console.WriteLine($"[DEBUG] End of message\n");
            }
        }

        public void FromApp(QuickFix.Message message, SessionID sessionID)
        {
            string msgType = message.Header.GetString(35);
            Console.WriteLine($"[GFI FIX] <<< App: {msgType}");

            try
            {
                Crack(message, sessionID);
            }
            catch (UnsupportedMessageType)
            {
                Console.WriteLine($"[GFI FIX] ⚠️  Unsupported message type: {msgType}");
                Console.WriteLine($"[GFI FIX] Message: {message.ToString().Replace("\x01", "|")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GFI FIX] ⚠️  Error processing message {msgType}: {ex.Message}");
                Console.WriteLine($"[GFI FIX] Stack: {ex.StackTrace}");
            }
        }

        #endregion

        #region Message Handlers

        public void OnMessage(QuickFix.FIX44.Quote quote, SessionID sessionID)
        {
            try
            {
                string lpName = quote.Header.GetString(Tags.OnBehalfOfCompID);
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
                string groupID = GetGroupIDForQuoteReqID(quoteReqID);

                _quotes.AddOrUpdate(key,
                    new StreamInfo
                    {
                        LP = lpName,
                        QuoteReqID = quoteReqID,
                        GroupID = groupID,
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

        public void OnMessage(QuickFix.FIX44.QuoteCancel message, SessionID sessionID)
        {
            try
            {
                // FIXED: Get LP from HEADER, not body!
                string lpName = "UNKNOWN";
                try
                {
                    lpName = message.Header.GetString(Tags.OnBehalfOfCompID);
                }
                catch
                {
                    // Fallback if not in header
                    if (message.IsSetField(Tags.OnBehalfOfCompID))
                    {
                        lpName = message.GetString(Tags.OnBehalfOfCompID);
                    }
                }

                // Extract QuoteReqID
                string quoteReqID = message.IsSetQuoteReqID()
                    ? message.QuoteReqID.getValue()
                    : "N/A";

                // Extract QuoteID (tag 117)
                string quoteID = "N/A";
                if (message.IsSetField(Tags.QuoteID))
                {
                    quoteID = message.GetString(Tags.QuoteID);
                }

                // Extract cancel type
                int cancelType = message.IsSetQuoteCancelType()
                    ? message.QuoteCancelType.getValue()
                    : 0;

                Console.WriteLine($"[GFI FIX] <<< Quote Cancel (35=Z)");
                Console.WriteLine($"  LP: {lpName}");
                Console.WriteLine($"  QuoteReqID: {quoteReqID}");
                Console.WriteLine($"  QuoteID: {quoteID}");
                Console.WriteLine($"  CancelType: {cancelType}");

                // Update your quotes dictionary to mark as canceled
                string key = $"{quoteReqID}_{lpName}";

                if (_quotes.TryGetValue(key, out var existingStream))
                {
                    // Determine which side is being canceled based on QuoteID pattern
                    if (quoteID.Contains("_b") || quoteID.StartsWith("B_"))
                    {
                        Console.WriteLine($"  → Canceling BID quote (replacement expected)");
                        existingStream.BidQuote = null; // Clear stale bid
                    }
                    else if (quoteID.Contains("_s") || quoteID.Contains("_o") || quoteID.Contains("-O") || quoteID.Contains("-T") || quoteID.StartsWith("O_"))
                    {
                        Console.WriteLine($"  → Canceling OFFER quote (replacement expected)");
                        existingStream.OfferQuote = null; // Clear stale offer
                    }
                    else
                    {
                        Console.WriteLine($"  → Quote canceled (awaiting replacement)");
                    }

                    existingStream.LastUpdate = DateTime.UtcNow;
                }
                else
                {
                    Console.WriteLine($"  ⚠ Warning: No existing quote found for key '{key}'");
                }

                // Notify UI that quote was canceled
                OnQuoteCanceled?.Invoke(quoteReqID, lpName, quoteID);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GFI FIX] ERROR processing Quote Cancel: {ex.Message}");
                Console.WriteLine($"[GFI FIX] Stack: {ex.StackTrace}");
            }
        }

        public void OnMessage(QuickFix.FIX44.QuoteStatusReport report, SessionID sessionID)
        {
            string quoteReqID = report.GetString(Tags.QuoteReqID);
            int quoteStatus = report.IsSetField(Tags.QuoteStatus) ? report.GetInt(Tags.QuoteStatus) : -1;

            Console.WriteLine($"\n[GFI FIX] <<< Quote Status Report (35=AI)");
            Console.WriteLine($"  QuoteReqID: {quoteReqID}");
            Console.WriteLine($"  QuoteStatus: {quoteStatus} ({GetQuoteStatusText(quoteStatus)})");

            if (report.IsSetField(58))
            {
                string text = report.GetString(58);
                Console.WriteLine($"  Text: {text}");
            }
        }

        public void OnMessage(QuickFix.FIX44.QuoteRequestReject reject, SessionID sessionID)
        {
            Console.WriteLine($"\n[GFI FIX] <<< QUOTE REQUEST REJECT (35=AG)");
            Console.WriteLine(new string('=', 60));

            if (reject.IsSetField(131))
            {
                string quoteReqID = reject.GetString(131);
                Console.WriteLine($"  QuoteReqID: {quoteReqID}");
            }

            if (reject.IsSetField(658))
            {
                int rejectReason = reject.GetInt(658);
                Console.WriteLine($"  RejectReason: {rejectReason} ({GetQuoteRequestRejectReasonText(rejectReason)})");
            }

            if (reject.IsSetField(58))
            {
                string rejectText = reject.GetString(58);
                Console.WriteLine($"  ⚠️  Reject Text: {rejectText}");
            }

            Console.WriteLine(new string('=', 60));
        }

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

                if (execReport.IsSetField(58))
                {
                    string text = execReport.GetString(58);
                    Console.WriteLine($"  Reason: {text}");
                }
            }
        }

        public void OnMessage(QuickFix.FIX44.BusinessMessageReject reject, SessionID sessionID)
        {
            Console.WriteLine($"\n[GFI FIX] <<< BUSINESS MESSAGE REJECT (35=j)");
            Console.WriteLine(new string('=', 60));

            if (reject.IsSetField(372))
            {
                string refMsgType = reject.GetString(372);
                Console.WriteLine($"  RefMsgType: {refMsgType}");
            }

            if (reject.IsSetField(380))
            {
                int rejectReason = reject.GetInt(380);
                Console.WriteLine($"  BusinessRejectReason: {rejectReason} ({GetBusinessRejectReasonText(rejectReason)})");
            }

            if (reject.IsSetField(58))
            {
                string rejectText = reject.GetString(58);
                Console.WriteLine($"  ⚠️  Reject Text: {rejectText}");
            }

            Console.WriteLine(new string('=', 60));
        }

        public void OnMessage(QuickFix.Message message, SessionID sessionID)
        {
            try
            {
                var msgType = message.Header.GetField(Tags.MsgType);

                if (msgType == "SD")
                {
                    Console.WriteLine($"\n[GFI FIX] <<< StaticData (35=SD) - LP Information");
                    Console.WriteLine(new string('-', 60));

                    if (message.IsSetField(1663))
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

                                string displayName = group.IsSetField(1402)
                                    ? group.GetString(1402)
                                    : "N/A";

                                string priceRequest = group.IsSetField(9996)
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

        public void RegisterQuoteRequest(string quoteReqID, string groupID)
        {
            _quoteReqToGroupID[quoteReqID] = groupID;
        }

        private string GetGroupIDForQuoteReqID(string quoteReqID)
        {
            return _quoteReqToGroupID.TryGetValue(quoteReqID, out string groupID) ? groupID : "unknown";
        }

        private void ParseRejectMessage(QuickFix.Message message)
        {
            try
            {
                Console.WriteLine($"\n[GFI FIX] === PARSING REJECT MESSAGE ===");

                if (message.IsSetField(45))
                {
                    int refSeqNum = message.GetInt(45);
                    Console.WriteLine($"  RefSeqNum: {refSeqNum}");
                }

                if (message.IsSetField(371))
                {
                    int refTagID = message.GetInt(371);
                    Console.WriteLine($"  RefTagID: {refTagID}");
                }

                if (message.IsSetField(372))
                {
                    string refMsgType = message.GetString(372);
                    Console.WriteLine($"  RefMsgType: {refMsgType}");
                }

                if (message.IsSetField(373))
                {
                    int reason = message.GetInt(373);
                    Console.WriteLine($"  SessionRejectReason: {reason} ({GetSessionRejectReasonText(reason)})");
                }

                if (message.IsSetField(58))
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
            msg.Set(Tags.OnBehalfOfCompID.ToString(), quote.Header.GetString(Tags.OnBehalfOfCompID));
            msg.Set(Tags.QuoteReqID.ToString(), quote.GetString(Tags.QuoteReqID));
            msg.Set(Tags.QuoteID.ToString(), quote.GetString(Tags.QuoteID));
            msg.Set(Tags.Side.ToString(), quote.GetString(Tags.Side));
            msg.Set(Tags.Symbol.ToString(), quote.GetString(Tags.Symbol));

            // DIRECT PREMIUM AND VOLATILITY (not in legs for GFI)
            Console.WriteLine($"  [DEBUG] Checking for direct premium/vol fields...");

            if (quote.IsSetField(5678)) // Volatility
            {
                string vol = quote.GetString(5678);
                msg.Set("leg1_5678", vol);
                Console.WriteLine($"  [DEBUG] Direct Vol (5678): {vol}");
            }

            if (quote.IsSetField(5844)) // Premium
            {
                string prem = quote.GetString(5844);
                msg.Set("leg1_5844", prem);
                Console.WriteLine($"  [DEBUG] Direct Premium (5844): {prem}");
            }

            // Try legs structure (might not exist)
            if (quote.IsSetField(Tags.NoLegs))
            {
                int noLegs = quote.GetInt(Tags.NoLegs);
                msg.Set(Tags.NoLegs.ToString(), noLegs.ToString());
                Console.WriteLine($"  [DEBUG] Found {noLegs} legs in repeating group");

                for (int i = 1; i <= noLegs; i++)
                {
                    var legGroup = quote.GetGroup(i, Tags.NoLegs);

                    if (legGroup.IsSetField(5844))
                    {
                        string legPrem = legGroup.GetString(5844);
                        msg.Set($"leg{i}_5844", legPrem);
                        Console.WriteLine($"    Leg {i} Premium: {legPrem}");
                    }

                    if (legGroup.IsSetField(5678))
                    {
                        string vol = legGroup.GetString(5678);
                        msg.Set($"leg{i}_5678", vol);
                        Console.WriteLine($"    Leg {i} Vol: {vol}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"  [DEBUG] No legs repeating group found - using direct fields");
            }

            return msg;
        }

        public List<StreamInfo> GetActiveStreams(string groupId)
        {
            var result = new List<StreamInfo>();

            foreach (var stream in _quotes.Values)
            {
                if (stream.GroupID == groupId)
                {
                    result.Add(stream);
                }
            }

            return result;
        }

        /// <summary>
        /// Validates if a quote is still valid for execution
        /// Per GFI FIX API spec: Uses ValidUntilTime (tag 62) to check quote expiration
        /// </summary>
        public (bool isValid, string reason) ValidateQuoteForExecution(string quoteReqID, string lpName, string side)
        {
            string key = $"{quoteReqID}_{lpName}";

            if (!_quotes.TryGetValue(key, out var stream))
            {
                return (false, $"Quote stream not found for {lpName}");
            }

            // Check if the specific side (bid/offer) is available
            FIXMessage quote = side == "BUY" ? stream.OfferQuote : stream.BidQuote;

            if (quote == null)
            {
                return (false, $"Quote for {side} side has been canceled or not yet received");
            }

            // Check ValidUntilTime (tag 62) per GFI spec
            // Format: YYYYMMDD-HH:MM:SS (e.g., "20081006-13:00:00")
            string validUntilTimeStr = quote.Get("62");

            if (!string.IsNullOrEmpty(validUntilTimeStr))
            {
                if (DateTime.TryParseExact(validUntilTimeStr, "yyyyMMdd-HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out DateTime validUntilTime))
                {
                    DateTime nowUtc = DateTime.UtcNow;

                    if (nowUtc > validUntilTime)
                    {
                        var expired = (nowUtc - validUntilTime).TotalSeconds;
                        return (false, $"Quote expired {expired:F1}s ago (ValidUntilTime: {validUntilTimeStr})");
                    }

                    // Optional: Warn if quote is about to expire in next 2 seconds
                    var timeRemaining = (validUntilTime - nowUtc).TotalSeconds;
                    if (timeRemaining < 2)
                    {
                        Console.WriteLine($"[GFI FIX] ⚠️  Quote expires in {timeRemaining:F1}s");
                    }
                }
                else
                {
                    Console.WriteLine($"[GFI FIX] ⚠️  Could not parse ValidUntilTime: {validUntilTimeStr}");
                }
            }
            else
            {
                // Fallback: If ValidUntilTime not present, use LastUpdate timestamp
                // GFI spec says API applies default time if not provided
                var age = DateTime.UtcNow - stream.LastUpdate;
                if (age.TotalSeconds > 30)  // Default 30s timeout
                {
                    return (false, $"Quote is stale (age: {age.TotalSeconds:F1}s, no ValidUntilTime)");
                }
            }

            return (true, "Quote is valid");
        }

        /// <summary>
        /// Gets a valid quote for execution, or null if unavailable
        /// </summary>
        public FIXMessage GetValidQuote(string quoteReqID, string lpName, string side)
        {
            var (isValid, reason) = ValidateQuoteForExecution(quoteReqID, lpName, side);

            if (!isValid)
            {
                Console.WriteLine($"[GFI FIX] ⚠️  Cannot execute: {reason}");
                return null;
            }

            string key = $"{quoteReqID}_{lpName}";
            if (_quotes.TryGetValue(key, out var stream))
            {
                return side == "BUY" ? stream.OfferQuote : stream.BidQuote;
            }

            return null;
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