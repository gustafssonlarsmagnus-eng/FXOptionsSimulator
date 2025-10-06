using System;
using System.Collections.Generic;

namespace FXOptionsSimulator
{
    /// <summary>
    /// Configuration matching GFI Fenics requirements
    /// These will be provided by GFI when you onboard
    /// </summary>
    public class FenicsConfig
    {
        // ===== PROVIDED BY GFI =====
        // You'll receive these during UAT onboarding

        public string Environment { get; set; } = "UAT"; // or "PROD"

        // Connection details
        public string Host { get; set; } = "quotes.stage2.gfifx.com"; // UAT
        public int Port { get; set; } = 443; // SSL

        // Your credentials (GFI assigns these)
        public string SenderCompID { get; set; } = "YOUR_COMPID_HERE"; // e.g., "CLIENT123"
        public string OnBehalfOfCompID { get; set; } = "YOUR_COMPID_HERE"; // Usually same
        public string Username { get; set; } = "your.username"; // For tag 553
        public string Password { get; set; } = "your.password"; // For tag 554

        // Your account prefix for QuoteReqID (tag 131)
        public string QuoteReqPrefix { get; set; } = "FENICS.5015500."; // GFI assigns this number

        // Heartbeat (must be <= 10 seconds per spec)
        public int HeartbeatInterval { get; set; } = 10;

        // ===== LIQUIDITY PROVIDERS =====
        // Available LPs and their CompIDs
        // NOTE: Values differ between UAT and PROD (e.g., NOMU vs NOMURA)
        public Dictionary<string, string> LiquidityProviders { get; set; } = new Dictionary<string, string>
        {
            // UAT CompIDs (examples - GFI will provide actual values)
            ["MS"] = "MS",
            ["UBS"] = "UBS",
            ["CITI"] = "CITI",
            ["JPM"] = "JPM",
            ["GS"] = "GS",
            ["BARC"] = "BARC",
            ["DB"] = "DB",
            ["NATWEST"] = "NATWEST",
            ["NOMU"] = "NOMU", // Nomura in UAT
            ["SG"] = "SG",
        };

        // Production would be different:
        // ["NOMURA"] = "NOMURA",  // Not NOMU

        // ===== MARKET DATA =====
        // Currency pairs you're entitled to trade
        public List<string> AuthorizedPairs { get; set; } = new List<string>
        {
            "EURUSD",
            "USDSEK",
            "EURSEK",
            "GBPUSD",
            "USDJPY",
        };

        // Cutoff mappings (tag 9125) - from Appendix I
        public Dictionary<string, string> CutoffCodes { get; set; } = new Dictionary<string, string>
        {
            ["NY"] = "1",      // New York 10:00
            ["TK"] = "2",      // Tokyo 15:00
            ["LON"] = "157",   // London WMR 1pm
        };

        // ===== VALIDATION =====
        public void Validate()
        {
            var errors = new List<string>();

            if (SenderCompID == "YOUR_COMPID_HERE")
                errors.Add("SenderCompID not configured - get from GFI");

            if (Username == "your.username")
                errors.Add("Username not configured - get from GFI");

            if (Password == "your.password")
                errors.Add("Password not configured - get from GFI");

            if (QuoteReqPrefix.Contains("5015500") && Environment == "PROD")
                errors.Add("Using UAT prefix in PROD environment");

            if (HeartbeatInterval > 10)
                errors.Add("Heartbeat must be <= 10 seconds per GFI spec");

            if (errors.Count > 0)
            {
                Console.WriteLine("\n=== CONFIGURATION ERRORS ===");
                foreach (var error in errors)
                {
                    Console.WriteLine($"  ! {error}");
                }
                Console.WriteLine("\nThese fields will be provided by GFI during onboarding.");
            }
        }

        public void PrintConfig()
        {
            Console.WriteLine("\n=== FENICS CONNECTION CONFIG ===");
            Console.WriteLine($"Environment:     {Environment}");
            Console.WriteLine($"Host:            {Host}");
            Console.WriteLine($"Port:            {Port}");
            Console.WriteLine($"SenderCompID:    {SenderCompID}");
            Console.WriteLine($"QuoteReqPrefix:  {QuoteReqPrefix}");
            Console.WriteLine($"Heartbeat:       {HeartbeatInterval}s");
            Console.WriteLine($"Available LPs:   {string.Join(", ", LiquidityProviders.Keys)}");
            Console.WriteLine($"Authorized Pairs: {string.Join(", ", AuthorizedPairs)}");
            Console.WriteLine();

            Validate();
        }
    }

    /// <summary>
    /// Realistic market data for testing
    /// Based on actual EURUSD levels as of Oct 2024
    /// </summary>
    public class MarketData
    {
        public string Symbol { get; set; }
        public double SpotRate { get; set; }
        public Dictionary<string, double> ForwardPoints { get; set; }
        public Dictionary<string, double> ImpliedVols { get; set; }

        public static MarketData GetEURUSD()
        {
            return new MarketData
            {
                Symbol = "EURUSD",
                SpotRate = 1.0850,
                ForwardPoints = new Dictionary<string, double>
                {
                    ["1M"] = 12.5,
                    ["3M"] = 35.2,
                    ["6M"] = 68.4,
                    ["1Y"] = 125.7,
                    ["2Y"] = 235.1,
                },
                ImpliedVols = new Dictionary<string, double>
                {
                    ["1M_ATM"] = 6.5,
                    ["3M_ATM"] = 7.2,
                    ["6M_ATM"] = 7.8,
                    ["1Y_ATM"] = 8.3,
                    ["2Y_ATM"] = 8.9,
                }
            };
        }

        public static MarketData GetUSDSEK()
        {
            return new MarketData
            {
                Symbol = "USDSEK",
                SpotRate = 10.4560,
                ForwardPoints = new Dictionary<string, double>
                {
                    ["1M"] = -85.5,
                    ["3M"] = -245.2,
                    ["6M"] = -482.1,
                    ["1Y"] = -895.3,
                },
                ImpliedVols = new Dictionary<string, double>
                {
                    ["1M_ATM"] = 8.5,
                    ["3M_ATM"] = 9.2,
                    ["6M_ATM"] = 9.8,
                    ["1Y_ATM"] = 10.5,
                }
            };
        }

        public double GetForwardRate(string tenor)
        {
            if (ForwardPoints.TryGetValue(tenor, out var points))
            {
                // Convert points to rate based on ccy pair convention
                var divisor = Symbol.StartsWith("USD") ? 10000.0 : 10000.0;
                return SpotRate + (points / divisor);
            }
            return SpotRate;
        }

        public double GetVolatility(string tenor, double delta = 50)
        {
            var key = $"{tenor}_ATM";
            if (ImpliedVols.TryGetValue(key, out var vol))
            {
                // Adjust for delta (simplified)
                if (delta != 50)
                {
                    var skew = 0.05 * Math.Abs(delta - 50) / 25.0; // Rough skew adjustment
                    vol += skew;
                }
                return vol;
            }
            return 8.0; // Default fallback
        }
    }
}