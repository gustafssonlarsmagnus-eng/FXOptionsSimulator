using QuickFix;
using QuickFix.Fields;
using QuickFix.Transport;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace FXOptionsSimulator.FIX
{
    public class GFIFIXSessionManager : MessageCracker, IApplication
    {
        private readonly SocketInitiator _initiator;
        private readonly SessionSettings _settings;
        private readonly IMessageStoreFactory _storeFactory;
        private readonly ILogFactory _logFactory;
        private readonly FenicsConfig _config; // ← Use your helper class
        
        private SessionID _sessionID;
        private bool _isLoggedOn;
        
        // Events
        public event EventHandler<QuoteReceivedEventArgs> QuoteReceived;
        public event EventHandler<ExecutionReportEventArgs> ExecutionReportReceived;
        public event EventHandler<string> QuoteRequestRejected;
        public event EventHandler<string> LogonSuccessful;
        public event EventHandler<string> LogonFailed;

        // Constructor with optional config
        public GFIFIXSessionManager(FenicsConfig config = null, string configFile = "quickfix.cfg")
        {
            _config = config ?? new FenicsConfig();
            _config.Validate(); // Show warnings if not configured
            
            _settings = new SessionSettings(configFile);
            
            // Override settings with FenicsConfig values
            OverrideSettingsFromConfig();
            
            _storeFactory = new FileStoreFactory(_settings);
            _logFactory = new FileLogFactory(_settings);
            
            _initiator = new SocketInitiator(this, _storeFactory, _settings, _logFactory);
        }

        private void OverrideSettingsFromConfig()
        {
            var sessionID = _settings.GetSessions().First();
            var dictionary = _settings.Get(sessionID);

            dictionary.SetString("SenderCompID", _config.SenderCompID);
            dictionary.SetString("OnBehalfOfCompID", _config.OnBehalfOfCompID);
            dictionary.SetString("TargetCompID", "GFI");
            dictionary.SetString("SocketConnectHost", _config.Host);
            dictionary.SetString("SocketConnectPort", _config.Port.ToString());  // ← Make sure it's .ToString()
            dictionary.SetString("Username", _config.Username);
            dictionary.SetString("Password", _config.Password);
            dictionary.SetString("HeartBtInt", _config.HeartbeatInterval.ToString());  // ← Make sure it's .ToString()
        }

        #region IApplication Implementation

        public void OnCreate(SessionID sessionID)
        {
            _sessionID = sessionID;
            Console.WriteLine($"FIX Session Created: {sessionID}");
        }

        public void OnLogon(SessionID sessionID)
        {
            _isLoggedOn = true;
            Console.WriteLine($"✅ FIX Session Logged On: {sessionID}");
            LogonSuccessful?.Invoke(this, $"Connected to GFI at {DateTime.UtcNow:u}");
        }

        public void OnLogout(SessionID sessionID)
        {
            _isLoggedOn = false;
            Console.WriteLine($"❌ FIX Session Logged Out: {sessionID}");
        }

        public void ToAdmin(Message message, SessionID sessionID)
        {
            if (message.Header.GetString(Tags.MsgType) == MsgType.LOGON)
            {
                message.SetField(new StringField(553, _config.Username));
                message.SetField(new StringField(554, _config.Password));
                // message.SetField(new OnBehalfOfCompID(_config.OnBehalfOfCompID));  // ← Not needed for logon
                message.SetField(new ResetSeqNumFlag(true));
                
                Console.WriteLine($"Sending Logon: User={_config.Username}");
            }
        }

        public void FromAdmin(Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetString(Tags.MsgType);
            Console.WriteLine($"Admin Message: {msgType}");
        }

        public void ToApp(Message message, SessionID sessionID)
        {
            var msgType = message.Header.GetString(Tags.MsgType);
            Console.WriteLine($"→ Sending: {msgType}");
        }

        public void FromApp(Message message, SessionID sessionID)
        {
            Crack(message, sessionID);
        }

        #endregion

        #region Connection Management

        public void Start()
        {
            Console.WriteLine($"Starting FIX connection to {_config.Host}:{_config.Port}...");
            _config.PrintConfig();
            _initiator.Start();
        }

        public void Stop()
        {
            _initiator.Stop();
        }

        public bool IsLoggedOn => _isLoggedOn;

        #endregion

        #region Send Quote Request (35=R)

        public async Task<string> SendQuoteRequestAsync(
            string lpName,              // "MS", "UBS", etc.
            string requestGroupID,      // e.g., "3-MyGroup"
            TradeStructure trade)
        {
            if (!_isLoggedOn)
                throw new InvalidOperationException("Not logged on to GFI");

            // Get LP CompID from config
            if (!_config.LiquidityProviders.TryGetValue(lpName, out var lpCompID))
                throw new ArgumentException($"Unknown LP: {lpName}");

            var quoteReqID = GenerateQuoteReqID();
            
            var msg = new QuickFix.FIX44.QuoteRequest();
            
            // Standard fields
            msg.SetField(new QuoteReqID(quoteReqID));
            msg.SetField(new DeliverToCompID(lpCompID));
            msg.SetField(new Symbol(trade.CurrencyPair));
            msg.SetField(new TransactTime(DateTime.UtcNow));
            
            // Custom fields
            msg.SetField(new IntField(8051, 1)); // NoBanksReqFenics
            msg.SetField(new StringField(8053, lpCompID)); // BankRequestedCompID
            msg.SetField(new IntField(9126, GetStructureCode(trade))); // Structure
            msg.SetField(new IntField(8505, 7)); // RegulationVenueType = OTC
            
            // Add more fields based on your TradeStructure
            // ... (legs, tenor, etc.)
            
            Console.WriteLine($"📤 Quote Request → {lpName}: {quoteReqID}");
            
            return await Task.Run(() =>
            {
                Session.SendToTarget(msg, _sessionID);
                return quoteReqID;
            });
        }

        private string GenerateQuoteReqID()
        {
            var unique = Guid.NewGuid().ToString("N").Substring(0, 12).ToUpper();
            return $"{_config.QuoteReqPrefix}{unique}";
        }

        #endregion

        #region Message Handlers (abbreviated - add full versions)

        public void OnMessage(QuickFix.FIX44.Quote quote, SessionID sessionID)
        {
            Console.WriteLine("📥 Quote received");
            // Parse and fire event
        }

        public void OnMessage(QuickFix.FIX44.QuoteStatusReport report, SessionID sessionID)
        {
            Console.WriteLine("📥 Quote Status Report");
        }

        public void OnMessage(QuickFix.FIX44.ExecutionReport execReport, SessionID sessionID)
        {
            Console.WriteLine("📥 Execution Report");
        }

        public void OnMessage(QuickFix.FIX44.QuoteRequestReject reject, SessionID sessionID)
        {
            Console.WriteLine("⚠️ Quote Request Rejected");
        }

        public void OnMessage(QuickFix.FIX44.BusinessMessageReject reject, SessionID sessionID)
        {
            Console.WriteLine("⚠️ Business Message Reject");
        }

        #endregion

        #region Helper Methods

        private int GetStructureCode(TradeStructure trade)
        {
            // Map your structure types to GFI codes
            return 1; // Placeholder
        }

        #endregion
    }

    // Event args classes
    public class QuoteReceivedEventArgs : EventArgs
    {
        public string QuoteID { get; set; }
        public string LPName { get; set; }
    }

    public class ExecutionReportEventArgs : EventArgs
    {
        public string OrderID { get; set; }
    }
}