using FXOptionsSimulator.FIX;
using QuickFix.Config;
using System;
using System.Collections.Generic;

namespace FXOptionsSimulator
{
    class Program
    {
        static void Main(string[] args)
        {
            // CRITICAL: Accept self-signed certificates for UAT
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            System.Net.ServicePointManager.ServerCertificateValidationCallback =
                (sender, cert, chain, sslPolicyErrors) => true; // Accept any certificate

            Console.WriteLine("=" + new string('=', 78));
            Console.WriteLine("FIX FX Options Trading Simulator - Live Ready Version");
            Console.WriteLine("Simulating GFI Fenics RFS Workflow with Realistic Data");
            Console.WriteLine("=" + new string('=', 78));

            Console.WriteLine("\n" + new string('=', 78));
            Console.WriteLine("TESTING FIX CONFIGURATION");
            Console.WriteLine(new string('=', 78));

            FenicsConfig config = null;  // ← DECLARE HERE, outside try block

            try
            {
                // Show configuration
                config = new FenicsConfig();  // ← ASSIGN HERE
                config.PrintConfig();

                // Test FIX session manager initialization
                Console.WriteLine("\n>>> Initializing FIX Session Manager...");
                var fixSession = new FXOptionsSimulator.FIX.GFIFIXSessionManager(config);

                Console.WriteLine("✅ FIX Session Manager created successfully!");
                Console.WriteLine("✅ Configuration files loaded (quickfix.cfg, FIX44.xml)");
                Console.WriteLine("✅ Ready to connect once GFI provides credentials");

                Console.WriteLine("\n⚠️  NEXT STEPS:");
                Console.WriteLine("   1. Email GFI to get: SenderCompID, Username, Password, QuoteReqID prefix");
                Console.WriteLine("   2. Update FenicsConfig.cs with real credentials");
                Console.WriteLine("   3. Test connection with: fixSession.Start()");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ FIX Setup Error: {ex.Message}");
                Console.WriteLine($"\nStack Trace:\n{ex.StackTrace}");
                Console.WriteLine("\nMake sure quickfix.cfg and FIX44.xml are in the output folder!");
                return;  // ← Exit if config fails
            }

            Console.WriteLine("\n" + new string('=', 78));
            Console.WriteLine("\nPress any key to continue...");
            Console.ReadKey();

            Console.WriteLine("\n" + new string('=', 78));
            Console.WriteLine("Choose what to test:");
            Console.WriteLine("  [1] Test REAL FIX Connection to GFI");
            Console.WriteLine("  [2] Test LOCAL FIX Server (Proof of Concept)");
            Console.WriteLine("  [3] Run Simulator Demo");
            Console.Write("\nYour choice: ");
            var choice = Console.ReadKey();
            Console.WriteLine("\n");

            if (choice.KeyChar == '1')
            {
                TestRealFIXConnection(config);
            }
            else if (choice.KeyChar == '2')
            {
                TestLocalFIXServer(config);  // NEW TEST MODE
            }
            else if (choice.KeyChar == '3')
            {
                RunRealisticDemo();
            }
            else
            {
                Console.WriteLine("Invalid choice. Exiting...");
            }

            Console.WriteLine("\n" + "=" + new string('=', 78));
            Console.WriteLine("Demo complete! Press any key to exit...");
            Console.ReadKey();
        }

        static void RunRealisticDemo()
        {
            var sim = new FIXSimulator();

            Console.WriteLine("\n" + new string('=', 78));
            Console.WriteLine("DEMO WITH YOUR OVML PARSER");
            Console.WriteLine(new string('=', 78));

            // Simulate your OVML parser output
            var ovmlResult = new
            {
                OVML = "OVML USDSEK 12/12/25 2L B,S 9.6000P,9.1500P N10M,50M VA SP9.3950",
                Underlying = "USDSEK",
                Expiry = "12/12/25",
                LegCount = 2,
                ParseMethod = "Regex-PutSpread"
            };

            Console.WriteLine($"\nOVML Parser Output:");
            Console.WriteLine($"  {ovmlResult.OVML}");
            Console.WriteLine($"  Method: {ovmlResult.ParseMethod}\n");

            // Convert to TradeStructure
            var trade = OVMLBridge.ConvertToTradeStructure(ovmlResult);
            trade.PrintSummary();

            Console.WriteLine("\n>>> Requesting quotes from 3 liquidity providers...");
            System.Threading.Thread.Sleep(1000);

            // Step 1: Send Quote Requests
            Console.WriteLine("\nSTEP 1: Sending Quote Requests");
            Console.WriteLine(new string('-', 78));

            var lps = new List<string> { "MS", "UBS", "NATWEST" };
            var (groupId, requests) = sim.SendQuoteRequest(
                underlying: trade.Underlying,
                lps: lps
            );

            System.Threading.Thread.Sleep(1500);

            // Step 2: Receive streaming quotes
            Console.WriteLine("\n\nSTEP 2: Receiving streaming quotes");
            Console.WriteLine(new string('-', 78));
            Console.WriteLine("(In real environment, quotes stream continuously until you cancel)");

            sim.StreamQuotes(groupId, numUpdates: 2, delayMs: 1500);

            // Step 3: Show best prices
            Console.WriteLine("\n\nSTEP 3: Market Analysis");
            Console.WriteLine(new string('-', 78));

            var (bestBid, bestOffer) = sim.GetBestPrices(groupId);

            if (bestBid != null && bestOffer != null)
            {
                var bidVol = double.Parse(bestBid.Get(TagStrings.Volatility));
                var offerVol = double.Parse(bestOffer.Get(TagStrings.Volatility));
                var spread = offerVol - bidVol;
                var midVol = (bidVol + offerVol) / 2;

                Console.WriteLine($"\nBest BID:   {bestBid.Get(TagStrings.OnBehalfOfCompID),-10} @ {bidVol:F3} vol");
                Console.WriteLine($"Best OFFER: {bestOffer.Get(TagStrings.OnBehalfOfCompID),-10} @ {offerVol:F3} vol");
                Console.WriteLine($"\nBid-Offer Spread: {spread:F3} vol ({spread / midVol * 10000:F0} bps)");
                Console.WriteLine($"Mid Market:       {midVol:F3} vol");

                // Decision logic
                Console.WriteLine("\n>>> Market looks tight. Ready to execute...");
                System.Threading.Thread.Sleep(2000);
            }

            // Step 4: Execute
            if (bestBid != null)
            {
                Console.WriteLine("\n\nSTEP 4: Executing Trade (SELL to hit BID)");
                Console.WriteLine(new string('-', 78));
                Console.WriteLine("NOTE: In real environment, LP has last-look and can reject within ~1 second");

                bool filled = sim.ExecuteTrade(bestBid, executionSide: "SELL");

                if (filled)
                {
                    Console.WriteLine("\n✓ TRADE FILLED");
                    Console.WriteLine($"  Counterparty: {bestBid.Get(TagStrings.OnBehalfOfCompID)}");
                    Console.WriteLine($"  Structure:    {trade.StructureType}");
                    Console.WriteLine($"  Notional:     {trade.Legs[0].NotionalMM}M {trade.Legs[0].NotionalCurrency}");
                    Console.WriteLine("\n>>> Next: You'll receive Trade Capture Report (35=AE) with full STP details");
                }
                else
                {
                    Console.WriteLine("\n✗ TRADE REJECTED (Last-Look)");
                    Console.WriteLine("  Common reasons:");
                    Console.WriteLine("    - Market moved");
                    Console.WriteLine("    - LP credit check");
                    Console.WriteLine("    - Quote became stale");
                    Console.WriteLine("\n>>> Would need to request new quotes");
                }
            }

            System.Threading.Thread.Sleep(1000);

            // Step 5: Cleanup
            Console.WriteLine("\n\nSTEP 5: Canceling quote streams");
            Console.WriteLine(new string('-', 78));
            Console.WriteLine("IMPORTANT: Always cancel streams when done to avoid unnecessary market data");

            foreach (var (lp, quoteReqId) in requests)
            {
                sim.CancelStream(quoteReqId);
            }

            // Summary
            Console.WriteLine("\n\n" + new string('=', 78));
            Console.WriteLine("KEY LEARNINGS FOR LIVE ENVIRONMENT");
            Console.WriteLine(new string('=', 78));
            Console.WriteLine(@"
1. CONFIGURATION
   - Get SenderCompID, Username, Password from GFI
   - Note your QuoteReqID prefix (e.g., FENICS.5015500.)
   - LP CompIDs differ between UAT and PROD

2. MESSAGE FLOW
   - One 35=R per LP (not one request to all)
   - Each LP streams 35=S continuously (bid and offer separately)
   - Group related requests with tag 8051
   - 35=AB to execute, 35=8 for confirmation
   - Always send 35=Z to cancel when done

3. TIMING
   - Quotes update 0.5-2 times per second
   - Last-look window ~1 second
   - ValidUntilTime on quotes (typically 30 seconds)
   - Heartbeat must be <= 10 seconds

4. ERROR HANDLING
   - 35=AG = Quote Request Rejected
   - 35=j = Business Message Reject (rate limiting, LP offline)
   - 35=8 with OrdStatus=8 = Execution rejected

5. NEXT STEPS
   - Connect your OVML parser to create TradeStructure
   - Install QuickFIX.Net for real FIX connectivity
   - Test in UAT before production
");
        }

        static void TestLocalFIXServer(FenicsConfig config)
        {
            Console.WriteLine("\n" + new string('=', 78));
            Console.WriteLine("TESTING LOCAL FIX SERVER");
            Console.WriteLine(new string('=', 78));

            config.IsTestMode = true;  // Enable test mode

            Console.WriteLine("✅ Configuration validated - connecting to local test server");

            var fixSession = new FXOptionsSimulator.FIX.GFIFIXSessionManager(config);

            Console.WriteLine("Starting FIX connection to localhost:9999...");
            fixSession.Start();

            Console.WriteLine("\n⏳ Waiting 10 seconds for logon...");
            System.Threading.Thread.Sleep(10000);

            fixSession.Stop();

            Console.WriteLine("\n" + new string('=', 78));
        }

        static void ShowMoreExamples()
        {
            Console.WriteLine("\n=== ADDITIONAL TRADE EXAMPLES ===\n");

            // Call spread
            var callSpread = TradeStructure.CreateCallSpread();
            callSpread.PrintSummary();

            System.Threading.Thread.Sleep(1000);

            // Risk reversal
            var rr = TradeStructure.CreateRiskReversal();
            rr.PrintSummary();

            System.Threading.Thread.Sleep(1000);

            // Seagull
            var seagull = TradeStructure.CreateSeagull();
            seagull.PrintSummary();

            Console.WriteLine("\n>>> These structures can be created from your OVML parser");
        }
        static void TestRealFIXConnection(FenicsConfig config)
        {
            Console.WriteLine("\n" + new string('=', 78));
            Console.WriteLine("TESTING REAL FIX CONNECTION TO GFI UAT");
            Console.WriteLine(new string('=', 78));

            var fixSession = new FXOptionsSimulator.FIX.GFIFIXSessionManager(config);

            fixSession.LogonSuccessful += (sender, msg) =>
                Console.WriteLine($"\n🎉🎉🎉 {msg} 🎉🎉🎉");

            fixSession.LogonFailed += (sender, msg) =>
                Console.WriteLine($"\n❌ LOGON FAILED: {msg}");

            // fixSession.StatusMessage += (sender, msg) => 
            //     Console.WriteLine($"[FIX] {msg}");

            try
            {
                Console.WriteLine("\n⏳ Starting FIX initiator...");
                Console.WriteLine("   Connecting to: quotes.stage2.gfifx.com:443");
                Console.WriteLine("   As: WEBFENICS55 (SWES)\n");

                fixSession.Start();

                Console.WriteLine("⏳ Waiting for logon response...");
                System.Threading.Thread.Sleep(10000);

                if (fixSession.IsLoggedOn)
                {
                    Console.WriteLine("\n" + new string('=', 78));
                    Console.WriteLine("SUCCESS! CONNECTED TO GFI UAT!");
                    Console.WriteLine(new string('=', 78));
                    Console.WriteLine("\nYou can now:");
                    Console.WriteLine("  ✅ Send quote requests to real LPs");
                    Console.WriteLine("  ✅ Receive live streaming quotes");
                    Console.WriteLine("  ✅ Execute real trades (in UAT)");
                    Console.WriteLine("\nPress any key to disconnect...");
                    Console.ReadKey();
                }
                else
                {
                    Console.WriteLine("\n⚠️ Connection timeout or logon failed");
                    Console.WriteLine("\nCheck logs in: ./fixlog/ folder");
                }

                fixSession.Stop();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ Connection Error: {ex.Message}");
            }
        }

    }
}