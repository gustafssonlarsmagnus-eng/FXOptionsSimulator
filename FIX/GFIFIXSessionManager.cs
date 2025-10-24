using QuickFix;
using QuickFix.Fields;
using QuickFix.Transport;
using QuickFix.FIX44;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FXOptionsSimulator.FIX
{
    public class GFIFIXSessionManager
    {
        private readonly SocketInitiator _initiator;
        private readonly SessionSettings _settings;
        private readonly GFIFIXApplication _application;
        private SessionID _sessionID;

        public GFIFIXApplication Application => _application;
        public bool IsLoggedOn => _application.IsLoggedOn;

        public GFIFIXSessionManager(string configFile = "quickfix.cfg")
        {
            Console.WriteLine($"[FIX Manager] Initializing with config: {configFile}");

            _application = new GFIFIXApplication();
            _settings = new SessionSettings(configFile);

            var storeFactory = new FileStoreFactory(_settings);
            var logFactory = new FileLogFactory(_settings);

            _initiator = new SocketInitiator(_application, storeFactory, _settings, logFactory);

            Console.WriteLine("[FIX Manager] Initialized successfully");
        }

        #region Connection Management

        public void Start()
        {
            Console.WriteLine("\n" + new string('=', 60));
            Console.WriteLine("STARTING FIX SESSION");
            Console.WriteLine(new string('=', 60));

            try
            {
                var sessions = _settings.GetSessions();
                if (sessions.Count == 0)
                {
                    throw new Exception("No sessions found in config file!");
                }

                _sessionID = sessions.First();
                var dict = _settings.Get(_sessionID);

                Console.WriteLine("\n[FIX Config]:");
                Console.WriteLine($"  SenderCompID: {dict.GetString("SenderCompID")}");
                Console.WriteLine($"  TargetCompID: {dict.GetString("TargetCompID")}");
                Console.WriteLine($"  Host: {dict.GetString("SocketConnectHost")}");
                Console.WriteLine($"  Port: {dict.GetString("SocketConnectPort")}");

                _initiator.Start();
                Console.WriteLine("\n[FIX Manager] ✓ Initiator started");
                Console.WriteLine("[FIX Manager] Waiting for logon...\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[FIX Manager] ✗ ERROR starting: {ex.Message}");
                throw;
            }
        }

        public void Stop()
        {
            Console.WriteLine("\n[FIX Manager] Stopping session...");
            _initiator.Stop();
            Console.WriteLine("[FIX Manager] ✓ Stopped");
        }

        #endregion

        #region Send Quote Request

        public string SendQuoteRequest(TradeStructure trade, string lpName, string groupId)
        {
            if (!IsLoggedOn)
            {
                throw new InvalidOperationException("Cannot send quote request - not logged on!");
            }

            string quoteReqID = $"FENICS.5015500.Q{DateTime.UtcNow.Ticks}";

            Console.WriteLine($"\n[FIX Manager] Building Quote Request for {lpName}");
            Console.WriteLine($"  QuoteReqID: {quoteReqID}");
            Console.WriteLine($"  GroupID: {groupId}");
            Console.WriteLine($"  Underlying: {trade.Underlying}");
            Console.WriteLine($"  Legs: {trade.Legs.Count}");

            try
            {
                var msg = BuildQuoteRequestMessage(trade, lpName, quoteReqID, groupId);
                Session.SendToTarget(msg, _sessionID);

                Console.WriteLine($"[FIX Manager] ✓ Quote Request sent to {lpName}");
                return quoteReqID;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FIX Manager] ✗ ERROR sending quote request: {ex.Message}");
                throw;
            }
        }

        private QuickFix.FIX44.QuoteRequest BuildQuoteRequestMessage(
     TradeStructure trade,
     string lpName,
     string quoteReqID,
     string groupId)
        {
            var msg = new QuickFix.FIX44.QuoteRequest();

            // HEADER
            msg.Header.SetField(new DeliverToCompID(lpName));

            // BODY FIELDS in exact order
            msg.SetField(new TradeDate(DateTime.UtcNow.ToString("yyyyMMdd")));
            msg.QuoteReqID = new QuoteReqID(quoteReqID);
            msg.SetField(new StringField(5475, "S"));
            msg.SetField(new StringField(5830, trade.PremiumCurrency));
            msg.SetField(new StringField(9016, "1"));

            int structureCode = GetStructureCode(trade.StructureType);
            msg.SetField(new StringField(9126, structureCode.ToString()));
            msg.SetField(new StringField(9943, "2"));

            // Build NoRelatedSym group manually
            var noRelatedSym = new QuickFix.FIX44.QuoteRequest.NoRelatedSymGroup();
            noRelatedSym.Symbol = new Symbol(trade.Underlying);
            noRelatedSym.SetField(new StringField(6258, structureCode.ToString()));
            noRelatedSym.SetField(new QuoteType(QuoteType.TRADEABLE));
            noRelatedSym.NoLegs = new NoLegs(trade.Legs.Count);

            // Build each leg with STRICT field order using only primitives
            for (int i = 0; i < trade.Legs.Count; i++)
            {
                var leg = trade.Legs[i];
                var legGroup = new QuickFix.FIX44.QuoteRequest.NoRelatedSymGroup.NoLegsGroup();

                // Use direct field assignment in exact order
                legGroup.SetField(new LegSymbol(trade.Underlying)); // 600
                legGroup.SetField(new StringField(6714, leg.OptionType == "CALL" ? "1" : "2")); // LegStrategy
                legGroup.SetField(new StringField(9125, "1")); // Cutoff
                legGroup.SetField(new StringField(6215, leg.Tenor)); // Tenor
                legGroup.SetField(new LegMaturityDate(leg.ExpiryDate.ToString("yyyyMMdd"))); // 611
                legGroup.SetField(new StringField(743, leg.ExpiryDate.AddDays(2).ToString("yyyyMMdd"))); // DeliveryDate
                legGroup.SetField(new StringField(5020, DateTime.UtcNow.AddDays(2).ToString("yyyyMMdd"))); // PremiumDelivery
                legGroup.SetField(new LegStrikePrice((decimal)leg.Strike)); // 612
                legGroup.SetField(new StringField(9019, "2")); // FXOptionStyle
                legGroup.SetField(new StringField(6351, (i == 0 || leg.Position == "SAME") ? "1" : "2")); // Position
                legGroup.SetField(new StringField(9904, "2")); // PriceIndicator

                if (trade.SpotReference > 0)
                {
                    legGroup.SetField(new StringField(5235, trade.SpotReference.ToString("F4"))); // LegSpotRate
                }

                legGroup.SetField(new LegCurrency(trade.PremiumCurrency)); // 556
                legGroup.SetField(new StringField(687, leg.NotionalMM.ToString())); // LegQty
                legGroup.SetField(new StringField(7940, leg.LegID)); // LegStrategyID
                legGroup.SetField(new StringField(9034, leg.NotionalCurrency)); // LegStrategyCcy

                noRelatedSym.AddGroup(legGroup);

                Console.WriteLine($"    Leg {i + 1}: {leg.Direction} {leg.OptionType} @ {leg.Strike} ({leg.NotionalMM}M)");
            }

            msg.AddGroup(noRelatedSym);

            return msg;
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

        #endregion

        #region Send Execution (35=AB)

        public void SendExecution(FIXMessage quote, string side)
        {
            if (!IsLoggedOn)
            {
                throw new InvalidOperationException("Cannot execute - not logged on!");
            }

            string quoteID = quote.Get(Tags.QuoteID.ToString());
            string quoteReqID = quote.Get(Tags.QuoteReqID.ToString());
            string clOrdID = $"ORD{DateTime.UtcNow.Ticks}";

            Console.WriteLine($"\n[FIX Manager] Executing trade");
            Console.WriteLine($"  ClOrdID: {clOrdID}");
            Console.WriteLine($"  QuoteID: {quoteID}");
            Console.WriteLine($"  Side: {side}");

            try
            {
                var msg = new QuickFix.FIX44.NewOrderMultileg();

                msg.ClOrdID = new ClOrdID(clOrdID);
                msg.SetField(new QuoteID(quoteID));
                msg.Side = new Side(side == "SELL" ? Side.SELL : Side.BUY);
                msg.SetField(new TransactTime(DateTime.UtcNow));
                msg.OrdType = new OrdType(OrdType.PREVIOUSLY_QUOTED);

                Session.SendToTarget(msg, _sessionID);

                Console.WriteLine($"[FIX Manager] ✓ Execution sent");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FIX Manager] ✗ ERROR executing: {ex.Message}");
                throw;
            }
        }

        #endregion
    }
}