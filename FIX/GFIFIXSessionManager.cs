﻿using QuickFix;
using QuickFix.Fields;
using QuickFix.Transport;
using QuickFix.FIX44;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FXOptionsSimulator.FIX
{
    public class GFIFIXSessionManager
    {
        private readonly SocketInitiator _initiator;
        private readonly SessionSettings _settings;
        private readonly GFIFIXApplication _application;
        private SessionID _sessionID;
        private RawFIXMessageBuilder _rawBuilder;  // NEW

        public GFIFIXApplication Application => _application;
        public bool IsLoggedOn => _application.IsLoggedOn;

        public GFIFIXSessionManager(string configFile = "quickfix.cfg")
        {
            Console.WriteLine($"[FIX Manager] Initializing with config: {configFile}");
            Console.WriteLine($"[FIX Manager] Config file path: {Path.GetFullPath(configFile)}");
            Console.WriteLine($"[FIX Manager] Config exists: {File.Exists(configFile)}");

            _application = new GFIFIXApplication();
            _rawBuilder = new RawFIXMessageBuilder("FIX.4.4", "WEBFENICS55", "GFI");  // NEW

            try
            {
                Console.WriteLine("[FIX Manager] Loading SessionSettings...");
                _settings = new SessionSettings(configFile);
                Console.WriteLine("[FIX Manager] SessionSettings loaded successfully");

                // Print all settings to see what's being parsed
                Console.WriteLine("\n[FIX Manager] === Config Contents ===");
                var sessions = _settings.GetSessions();

                Console.WriteLine($"[FIX Manager] Number of sessions: {sessions.Count}");

                foreach (var sessionID in sessions)
                {
                    Console.WriteLine($"[FIX Manager] Session: {sessionID}");
                    var dict = _settings.Get(sessionID);

                    // Try to read common settings
                    string[] commonKeys = {
                        "BeginString", "SenderCompID", "TargetCompID",
                        "SocketConnectHost", "SocketConnectPort", "HeartBtInt",
                        "ConnectionType", "OnBehalfOfCompID", "ResetOnLogon",
                        "EncryptMethod", "ReconnectInterval", "StartTime", "EndTime"
                    };

                    foreach (var key in commonKeys)
                    {
                        try
                        {
                            if (dict.Has(key))
                            {
                                var value = dict.GetString(key);
                                Console.WriteLine($"  {key} = {value}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"  {key} = ERROR: {ex.Message}");
                        }
                    }
                }
                Console.WriteLine("[FIX Manager] === End Config Contents ===\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FIX Manager] ✗ ERROR loading config: {ex.Message}");
                Console.WriteLine($"[FIX Manager] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[FIX Manager] Stack trace: {ex.StackTrace}");
                throw;
            }

            try
            {
                Console.WriteLine("[FIX Manager] Creating MemoryStoreFactory (no file storage)...");
                var storeFactory = new MemoryStoreFactory();
                Console.WriteLine("[FIX Manager] ✓ MemoryStoreFactory created");

                Console.WriteLine("[FIX Manager] Creating ScreenLogFactory (console logging)...");
                var logFactory = new ScreenLogFactory(_settings);
                Console.WriteLine("[FIX Manager] ✓ ScreenLogFactory created");

                Console.WriteLine("[FIX Manager] Creating SocketInitiator...");
                _initiator = new SocketInitiator(_application, storeFactory, _settings, logFactory);
                Console.WriteLine("[FIX Manager] ✓ SocketInitiator created");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FIX Manager] ✗ ERROR creating initiator: {ex.Message}");
                Console.WriteLine($"[FIX Manager] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[FIX Manager] Inner exception: {ex.InnerException?.Message}");
                Console.WriteLine($"[FIX Manager] Stack trace: {ex.StackTrace}");
                throw;
            }

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

                Console.WriteLine($"\n[FIX Manager] About to call _initiator.Start()...");
                Console.WriteLine($"[FIX Manager] Initiator is null: {_initiator == null}");
                Console.WriteLine($"[FIX Manager] SessionID: {_sessionID}");

                try
                {
                    _initiator.Start();
                    Console.WriteLine("\n[FIX Manager] ✓ Initiator started successfully");
                }
                catch (Exception startEx)
                {
                    Console.WriteLine($"\n[FIX Manager] ✗ ERROR in _initiator.Start()");
                    Console.WriteLine($"[FIX Manager] Error message: {startEx.Message}");
                    Console.WriteLine($"[FIX Manager] Exception type: {startEx.GetType().Name}");

                    if (startEx.InnerException != null)
                    {
                        Console.WriteLine($"\n[FIX Manager] === INNER EXCEPTION ===");
                        Console.WriteLine($"[FIX Manager] Inner message: {startEx.InnerException.Message}");
                        Console.WriteLine($"[FIX Manager] Inner type: {startEx.InnerException.GetType().Name}");

                        if (startEx.InnerException.InnerException != null)
                        {
                            Console.WriteLine($"\n[FIX Manager] === INNER INNER EXCEPTION ===");
                            Console.WriteLine($"[FIX Manager] Inner inner message: {startEx.InnerException.InnerException.Message}");
                            Console.WriteLine($"[FIX Manager] Inner inner type: {startEx.InnerException.InnerException.GetType().Name}");
                        }
                    }

                    Console.WriteLine($"\n[FIX Manager] === FULL STACK TRACE ===");
                    Console.WriteLine(startEx.StackTrace);

                    throw;
                }

                Console.WriteLine("[FIX Manager] Waiting for logon...\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[FIX Manager] ✗ ERROR starting session: {ex.Message}");
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
                // Get current sequence number and increment for next message
                var session = Session.LookupSession(_sessionID);
                int seqNum = session.NextSenderMsgSeqNum;
                _rawBuilder.SetMsgSeqNum(seqNum);

                // Build raw message with exact field order
                string rawMessage = _rawBuilder.BuildQuoteRequest(trade, lpName, quoteReqID, groupId);

                Console.WriteLine($"\n[DEBUG] Raw Quote Request Message (SeqNum={seqNum}):");
                Console.WriteLine($"{rawMessage.Replace("\x01", "|")}");
                Console.WriteLine($"[DEBUG] End of message\n");

                // Send raw message
                bool sent = session.Send(rawMessage);

                if (sent)
                {
                    // Manually increment the sequence number since we sent raw
                    session.NextSenderMsgSeqNum = seqNum + 1;
                    Console.WriteLine($"[FIX Manager] ✓ Quote Request sent to {lpName} (SeqNum={seqNum})");
                }
                else
                {
                    Console.WriteLine($"[FIX Manager] ✗ Failed to send message");
                }

                for (int i = 0; i < trade.Legs.Count; i++)
                {
                    var leg = trade.Legs[i];
                    Console.WriteLine($"    Leg {i + 1}: {leg.Direction} {leg.OptionType} @ {leg.Strike} ({leg.NotionalMM}M)");
                }

                return quoteReqID;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FIX Manager] ✗ ERROR sending quote request: {ex.Message}");
                Console.WriteLine($"[FIX Manager] Stack trace: {ex.StackTrace}");
                throw;
            }
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