using System;
using System.Text;

namespace FXOptionsSimulator.FIX
{
    /// <summary>
    /// Builds raw FIX messages with complete control over field order
    /// </summary>
    public class RawFIXMessageBuilder
    {
        private readonly StringBuilder _body;
        private readonly string _beginString;
        private readonly string _senderCompID;
        private readonly string _targetCompID;
        private int _msgSeqNum;

        public RawFIXMessageBuilder(string beginString, string senderCompID, string targetCompID)
        {
            _beginString = beginString;
            _senderCompID = senderCompID;
            _targetCompID = targetCompID;
            _body = new StringBuilder();
            _msgSeqNum = 1;
        }

        public void SetMsgSeqNum(int seqNum)
        {
            _msgSeqNum = seqNum;
        }

        public string BuildQuoteRequest(
            TradeStructure trade,
            string lpName,
            string quoteReqID,
            string groupId)
        {
            _body.Clear();

            // Standard header fields (in body)
            AddField(35, "R"); // MsgType
            AddField(34, _msgSeqNum.ToString()); // MsgSeqNum
            AddField(49, _senderCompID); // SenderCompID
            AddField(52, GetUTCTimestamp()); // SendingTime
            AddField(56, _targetCompID); // TargetCompID

            // DeliverToCompID in header
            AddField(128, lpName);

            // Body fields in EXACT GFI order
            AddField(75, DateTime.UtcNow.ToString("yyyyMMdd")); // TradeDate
            AddField(131, quoteReqID); // QuoteReqID
            AddField(5475, "S"); // PremDel
            AddField(5830, trade.PremiumCurrency); // PremiumCcy
            AddField(9016, "1"); // HedgeTradeType

            int structureCode = GetStructureCode(trade.StructureType);
            AddField(9126, structureCode.ToString()); // Structure
            AddField(9943, "2"); // ProductQuoteType
            AddField(146, "1"); // NoRelatedSym

            // NoRelatedSym group - EXACT order from GFI sample
            AddField(55, trade.Underlying); // Symbol
            AddField(6258, structureCode.ToString()); // Strategy - MOVED HERE!
            AddField(537, "1"); // QuoteType
            AddField(555, trade.Legs.Count.ToString()); // NoLegs

            // Legs in EXACT GFI order from their sample
            for (int i = 0; i < trade.Legs.Count; i++)
            {
                var leg = trade.Legs[i];

                // EXACT order from GFI sample
                AddField(600, trade.Underlying); // LegSymbol
                AddField(6714, leg.OptionType == "CALL" ? "1" : "2"); // LegStrategy
                AddField(9125, "1"); // Cutoff
                AddField(6215, leg.Tenor); // Tenor
                AddField(611, leg.ExpiryDate.ToString("yyyyMMdd")); // LegMaturityDate
                AddField(743, leg.ExpiryDate.AddDays(2).ToString("yyyyMMdd")); // DeliveryDate
                AddField(5020, DateTime.UtcNow.AddDays(2).ToString("yyyyMMdd")); // PremiumDelivery
                AddField(612, leg.Strike.ToString("F4")); // LegStrikePrice
                AddField(9019, "2"); // FXOptionStyle
                AddField(6351, (i == 0 || leg.Position == "SAME") ? "1" : "2"); // Position
                AddField(9904, "2"); // PriceIndicator

                if (trade.SpotReference > 0)
                {
                    AddField(5235, trade.SpotReference.ToString("F4")); // LegSpotRate
                }

                AddField(556, trade.PremiumCurrency); // LegCurrency
                AddField(687, leg.NotionalMM.ToString()); // LegQty
                AddField(7940, leg.LegID); // LegStrategyID
                AddField(9034, leg.NotionalCurrency); // LegStrategyCcy
            }

            // Build complete message with header and trailer
            return BuildCompleteMessage(_body.ToString());
        }

        private void AddField(int tag, string value)
        {
            _body.Append(tag);
            _body.Append('=');
            _body.Append(value);
            _body.Append('\x01'); // SOH delimiter
        }

        private string BuildCompleteMessage(string body)
        {
            // Calculate body length (without BeginString and BodyLength fields)
            int bodyLength = Encoding.ASCII.GetByteCount(body);

            // Build message without checksum
            var msgBuilder = new StringBuilder();
            msgBuilder.Append("8=");
            msgBuilder.Append(_beginString);
            msgBuilder.Append('\x01');
            msgBuilder.Append("9=");
            msgBuilder.Append(bodyLength);
            msgBuilder.Append('\x01');
            msgBuilder.Append(body);

            // Calculate checksum
            string msgWithoutChecksum = msgBuilder.ToString();
            string checksum = CalculateChecksum(msgWithoutChecksum);

            // Add checksum
            msgBuilder.Append("10=");
            msgBuilder.Append(checksum);
            msgBuilder.Append('\x01');

            return msgBuilder.ToString();
        }

        private string CalculateChecksum(string message)
        {
            int sum = 0;
            foreach (char c in message)
            {
                sum += c;
            }
            int checksum = sum % 256;
            return checksum.ToString("D3");
        }

        private string GetUTCTimestamp()
        {
            return DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss.fff");
        }

        private int GetStructureCode(string structureType)
        {
            return structureType switch
            {
                "Vanilla" => 1,
                "CallSpread" => 8,
                "PutSpread" => 9,
                "RiskReversal" => 5,
                "Seagull" => 10,
                _ => 1
            };
        }
    }
}