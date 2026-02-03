/*
 * QUANTCONNECT.COM - Democratizing Finance, Empowering Individuals.
 * Lean Algorithmic Trading Engine v2.0. Copyright 2014 QuantConnect Corporation.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
*/

using System;
using System.Collections.Generic;
using QuantConnect.Brokerages;

namespace QuantConnect.Brokerages.IG
{
    /// <summary>
    /// Provides symbol mapping between LEAN symbols and IG Markets EPIC codes
    /// </summary>
    /// <remarks>
    /// IG uses EPIC codes to identify instruments. The format is typically:
    /// - Forex: CS.D.{BASE}{QUOTE}.{CONTRACT}.IP (e.g., CS.D.EURUSD.MINI.IP)
    /// - Indices: IX.D.{INDEX}.{CONTRACT}.IP (e.g., IX.D.FTSE.DAILY.IP)
    /// - Commodities: CC.D.{COMMODITY}.{TYPE}.IP (e.g., CC.D.CL.USS.IP)
    /// - Crypto: CS.D.{CRYPTO}.CFD.IP (e.g., CS.D.BITCOIN.CFD.IP)
    /// </remarks>
    public class IGSymbolMapper : ISymbolMapper
    {
        // Comprehensive Forex pair mappings - Major, Minor, and Exotic pairs
        private static readonly Dictionary<string, string> ForexEpicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Major pairs (USD crosses)
            { "EURUSD", "CS.D.EURUSD.MINI.IP" },
            { "GBPUSD", "CS.D.GBPUSD.MINI.IP" },
            { "USDJPY", "CS.D.USDJPY.MINI.IP" },
            { "USDCHF", "CS.D.USDCHF.MINI.IP" },
            { "AUDUSD", "CS.D.AUDUSD.MINI.IP" },
            { "USDCAD", "CS.D.USDCAD.MINI.IP" },
            { "NZDUSD", "CS.D.NZDUSD.MINI.IP" },

            // Minor pairs (EUR crosses)
            { "EURGBP", "CS.D.EURGBP.MINI.IP" },
            { "EURJPY", "CS.D.EURJPY.MINI.IP" },
            { "EURCHF", "CS.D.EURCHF.MINI.IP" },
            { "EURAUD", "CS.D.EURAUD.MINI.IP" },
            { "EURCAD", "CS.D.EURCAD.MINI.IP" },
            { "EURNZD", "CS.D.EURNZD.MINI.IP" },
            { "EURSEK", "CS.D.EURSEK.MINI.IP" },
            { "EURNOK", "CS.D.EURNOK.MINI.IP" },
            { "EURDKK", "CS.D.EURDKK.MINI.IP" },
            { "EURPLN", "CS.D.EURPLN.MINI.IP" },
            { "EURHUF", "CS.D.EURHUF.MINI.IP" },
            { "EURCZK", "CS.D.EURCZK.MINI.IP" },
            { "EURTRY", "CS.D.EURTRY.MINI.IP" },
            { "EURZAR", "CS.D.EURZAR.MINI.IP" },

            // GBP crosses
            { "GBPJPY", "CS.D.GBPJPY.MINI.IP" },
            { "GBPCHF", "CS.D.GBPCHF.MINI.IP" },
            { "GBPAUD", "CS.D.GBPAUD.MINI.IP" },
            { "GBPCAD", "CS.D.GBPCAD.MINI.IP" },
            { "GBPNZD", "CS.D.GBPNZD.MINI.IP" },
            { "GBPSEK", "CS.D.GBPSEK.MINI.IP" },
            { "GBPNOK", "CS.D.GBPNOK.MINI.IP" },
            { "GBPDKK", "CS.D.GBPDKK.MINI.IP" },
            { "GBPPLN", "CS.D.GBPPLN.MINI.IP" },
            { "GBPZAR", "CS.D.GBPZAR.MINI.IP" },

            // AUD crosses
            { "AUDJPY", "CS.D.AUDJPY.MINI.IP" },
            { "AUDCHF", "CS.D.AUDCHF.MINI.IP" },
            { "AUDCAD", "CS.D.AUDCAD.MINI.IP" },
            { "AUDNZD", "CS.D.AUDNZD.MINI.IP" },
            { "AUDSEK", "CS.D.AUDSEK.MINI.IP" },
            { "AUDNOK", "CS.D.AUDNOK.MINI.IP" },
            { "AUDDKK", "CS.D.AUDDKK.MINI.IP" },
            { "AUDSGD", "CS.D.AUDSGD.MINI.IP" },
            { "AUDHKD", "CS.D.AUDHKD.MINI.IP" },

            // NZD crosses
            { "NZDJPY", "CS.D.NZDJPY.MINI.IP" },
            { "NZDCHF", "CS.D.NZDCHF.MINI.IP" },
            { "NZDCAD", "CS.D.NZDCAD.MINI.IP" },
            { "NZDSEK", "CS.D.NZDSEK.MINI.IP" },
            { "NZDNOK", "CS.D.NZDNOK.MINI.IP" },
            { "NZDSGD", "CS.D.NZDSGD.MINI.IP" },
            { "NZDHKD", "CS.D.NZDHKD.MINI.IP" },

            // CHF crosses
            { "CHFJPY", "CS.D.CHFJPY.MINI.IP" },
            { "CHFSEK", "CS.D.CHFSEK.MINI.IP" },
            { "CHFNOK", "CS.D.CHFNOK.MINI.IP" },
            { "CHFSGD", "CS.D.CHFSGD.MINI.IP" },

            // CAD crosses
            { "CADJPY", "CS.D.CADJPY.MINI.IP" },
            { "CADCHF", "CS.D.CADCHF.MINI.IP" },
            { "CADNOK", "CS.D.CADNOK.MINI.IP" },
            { "CADSEK", "CS.D.CADSEK.MINI.IP" },
            { "CADSGD", "CS.D.CADSGD.MINI.IP" },

            // JPY crosses
            { "NOKJPY", "CS.D.NOKJPY.MINI.IP" },
            { "SEKJPY", "CS.D.SEKJPY.MINI.IP" },
            { "SGDJPY", "CS.D.SGDJPY.MINI.IP" },
            { "ZARJPY", "CS.D.ZARJPY.MINI.IP" },
            { "HKDJPY", "CS.D.HKDJPY.MINI.IP" },

            // Exotic pairs
            { "USDMXN", "CS.D.USDMXN.MINI.IP" },
            { "USDZAR", "CS.D.USDZAR.MINI.IP" },
            { "USDTRY", "CS.D.USDTRY.MINI.IP" },
            { "USDSEK", "CS.D.USDSEK.MINI.IP" },
            { "USDNOK", "CS.D.USDNOK.MINI.IP" },
            { "USDDKK", "CS.D.USDDKK.MINI.IP" },
            { "USDPLN", "CS.D.USDPLN.MINI.IP" },
            { "USDHUF", "CS.D.USDHUF.MINI.IP" },
            { "USDCZK", "CS.D.USDCZK.MINI.IP" },
            { "USDSGD", "CS.D.USDSGD.MINI.IP" },
            { "USDHKD", "CS.D.USDHKD.MINI.IP" },
            { "USDCNH", "CS.D.USDCNH.MINI.IP" },
            { "USDTHB", "CS.D.USDTHB.MINI.IP" },
            { "USDINR", "CS.D.USDINR.MINI.IP" },
            { "USDIDR", "CS.D.USDIDR.MINI.IP" },
            { "USDKRW", "CS.D.USDKRW.MINI.IP" },
            { "USDRUB", "CS.D.USDRUB.MINI.IP" },
            { "USDBRL", "CS.D.USDBRL.MINI.IP" },
            { "USDARS", "CS.D.USDARS.MINI.IP" },
            { "USDCLP", "CS.D.USDCLP.MINI.IP" },
            { "USDCOP", "CS.D.USDCOP.MINI.IP" },
        };

        // Comprehensive Index mappings - Global equity indices
        private static readonly Dictionary<string, string> IndexEpicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // US Indices
            { "SPX", "IX.D.SPTRD.DAILY.IP" },           // S&P 500
            { "SPX500", "IX.D.SPTRD.DAILY.IP" },        // S&P 500 (alias)
            { "NDX", "IX.D.NASDAQ.DAILY.IP" },          // NASDAQ 100
            { "NAS100", "IX.D.NASDAQ.DAILY.IP" },       // NASDAQ 100 (alias)
            { "DJI", "IX.D.DOW.DAILY.IP" },             // Dow Jones Industrial Average
            { "US30", "IX.D.DOW.DAILY.IP" },            // Dow Jones (alias)
            { "RUT", "IX.D.RUSSELL.DAILY.IP" },         // Russell 2000
            { "US2000", "IX.D.RUSSELL.DAILY.IP" },      // Russell 2000 (alias)
            { "VIX", "IX.D.VIX.DAILY.IP" },             // Volatility Index
            { "SPY", "IX.D.SPTRD.DAILY.IP" },           // S&P 500 ETF mapping

            // UK Indices
            { "FTSE", "IX.D.FTSE.DAILY.IP" },           // FTSE 100
            { "UK100", "IX.D.FTSE.DAILY.IP" },          // FTSE 100 (alias)
            { "FTSE250", "IX.D.FTSE250.DAILY.IP" },     // FTSE 250

            // European Indices
            { "DAX", "IX.D.DAX.DAILY.IP" },             // DAX 30 (Germany)
            { "GER30", "IX.D.DAX.DAILY.IP" },           // DAX (alias)
            { "CAC", "IX.D.CAC.DAILY.IP" },             // CAC 40 (France)
            { "FRA40", "IX.D.CAC.DAILY.IP" },           // CAC 40 (alias)
            { "IBEX", "IX.D.IBEX.DAILY.IP" },           // IBEX 35 (Spain)
            { "ESP35", "IX.D.IBEX.DAILY.IP" },          // IBEX (alias)
            { "MIB", "IX.D.MIB.DAILY.IP" },             // FTSE MIB (Italy)
            { "ITA40", "IX.D.MIB.DAILY.IP" },           // FTSE MIB (alias)
            { "AEX", "IX.D.AEX.DAILY.IP" },             // AEX (Netherlands)
            { "NED25", "IX.D.AEX.DAILY.IP" },           // AEX (alias)
            { "SMI", "IX.D.SMI.DAILY.IP" },             // SMI (Switzerland)
            { "SUI20", "IX.D.SMI.DAILY.IP" },           // SMI (alias)
            { "STOXX50E", "IX.D.STOXX50.DAILY.IP" },    // Euro Stoxx 50
            { "EU50", "IX.D.STOXX50.DAILY.IP" },        // Euro Stoxx 50 (alias)
            { "OMX", "IX.D.OMX.DAILY.IP" },             // OMX Stockholm 30

            // Asian Indices
            { "N225", "IX.D.NIKKEI.DAILY.IP" },         // Nikkei 225 (Japan)
            { "JPN225", "IX.D.NIKKEI.DAILY.IP" },       // Nikkei 225 (alias)
            { "HSI", "IX.D.HANGSENG.DAILY.IP" },        // Hang Seng (Hong Kong)
            { "HKG50", "IX.D.HANGSENG.DAILY.IP" },      // Hang Seng (alias)
            { "SSEC", "IX.D.SHANGHAI.DAILY.IP" },       // Shanghai Composite
            { "CHN50", "IX.D.CHINA50.DAILY.IP" },       // China A50
            { "KOSPI", "IX.D.KOSPI.DAILY.IP" },         // KOSPI (South Korea)
            { "KOR200", "IX.D.KOSPI.DAILY.IP" },        // KOSPI (alias)
            { "ASX", "IX.D.ASX.DAILY.IP" },             // ASX 200 (Australia)
            { "AUS200", "IX.D.ASX.DAILY.IP" },          // ASX 200 (alias)
            { "NZX", "IX.D.NZ50.DAILY.IP" },            // NZX 50 (New Zealand)
            { "STI", "IX.D.STI.DAILY.IP" },             // Straits Times Index (Singapore)
            { "SGP30", "IX.D.STI.DAILY.IP" },           // STI (alias)
            { "SENSEX", "IX.D.SENSEX.DAILY.IP" },       // BSE Sensex (India)
            { "IND50", "IX.D.INDIA50.DAILY.IP" },       // India 50
            { "NIFTY", "IX.D.NIFTY.DAILY.IP" },         // Nifty 50 (India)
            { "TWII", "IX.D.TAIWAN.DAILY.IP" },         // Taiwan Weighted

            // Other Regional Indices
            { "BOVESPA", "IX.D.BOVESPA.DAILY.IP" },     // Bovespa (Brazil)
            { "BRA50", "IX.D.BOVESPA.DAILY.IP" },       // Bovespa (alias)
            { "IPC", "IX.D.IPC.DAILY.IP" },             // IPC (Mexico)
            { "MEX35", "IX.D.IPC.DAILY.IP" },           // IPC (alias)
            { "JSE", "IX.D.JSE40.DAILY.IP" },           // JSE Top 40 (South Africa)
            { "ZAF40", "IX.D.JSE40.DAILY.IP" },         // JSE (alias)
        };

        // Comprehensive Crypto mappings
        private static readonly Dictionary<string, string> CryptoEpicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Major Cryptocurrencies
            { "BTCUSD", "CS.D.BITCOIN.CFD.IP" },
            { "BTCEUR", "CS.D.BITCOINEU.CFD.IP" },
            { "BTCGBP", "CS.D.BITCOINGB.CFD.IP" },
            { "ETHUSD", "CS.D.ETHUSD.CFD.IP" },
            { "ETHEUR", "CS.D.ETHEUR.CFD.IP" },
            { "ETHGBP", "CS.D.ETHGBP.CFD.IP" },
            { "LTCUSD", "CS.D.LTCUSD.CFD.IP" },
            { "LTCEUR", "CS.D.LTCEUR.CFD.IP" },
            { "XRPUSD", "CS.D.XRPUSD.CFD.IP" },
            { "XRPEUR", "CS.D.XRPEUR.CFD.IP" },
            { "BCHUSD", "CS.D.BCHUSD.CFD.IP" },
            { "BCHEUR", "CS.D.BCHEUR.CFD.IP" },
            { "EOSUSD", "CS.D.EOSUSD.CFD.IP" },
            { "XLMUSD", "CS.D.XLMUSD.CFD.IP" },
            { "ADAUSD", "CS.D.CARDANO.CFD.IP" },
            { "ADAEUR", "CS.D.CARDANOEU.CFD.IP" },
            { "DOTUSD", "CS.D.POLKADOT.CFD.IP" },
            { "DOTEUR", "CS.D.POLKADOTEU.CFD.IP" },
            { "LINKUSD", "CS.D.CHAINLINK.CFD.IP" },
            { "LINKEUR", "CS.D.CHAINLINKEU.CFD.IP" },
            { "SOLUSD", "CS.D.SOLANA.CFD.IP" },
            { "SOLEUR", "CS.D.SOLANAEU.CFD.IP" },
            { "MATICUSD", "CS.D.POLYGON.CFD.IP" },
            { "AVAXUSD", "CS.D.AVALANCHE.CFD.IP" },
            { "UNIUSD", "CS.D.UNISWAP.CFD.IP" },
            { "DOGEUSD", "CS.D.DOGE.CFD.IP" },
            { "DOGEEUR", "CS.D.DOGEEUR.CFD.IP" },
            { "SHIBUSD", "CS.D.SHIB.CFD.IP" },
            { "TRXUSD", "CS.D.TRON.CFD.IP" },
            { "ATOMUSD", "CS.D.COSMOS.CFD.IP" },
            { "XTZUSD", "CS.D.TEZOS.CFD.IP" },
            { "VETUSD", "CS.D.VECHAIN.CFD.IP" },
            { "ALGOUSD", "CS.D.ALGO.CFD.IP" },
            { "FILUSD", "CS.D.FILECOIN.CFD.IP" },
            { "APTUSD", "CS.D.APTOS.CFD.IP" },
        };

        // Comprehensive Commodity mappings - Metals, Energy, Agriculture
        private static readonly Dictionary<string, string> CommodityEpicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Precious Metals
            { "XAUUSD", "CS.D.USCGC.TODAY.IP" },        // Gold vs USD (Spot)
            { "XAGUSD", "CS.D.USCSI.TODAY.IP" },        // Silver vs USD (Spot)
            { "XPTUSD", "CS.D.USCPT.TODAY.IP" },        // Platinum vs USD
            { "XPDUSD", "CS.D.USCPD.TODAY.IP" },        // Palladium vs USD
            { "GC", "CC.D.GC.USS.IP" },                 // Gold Futures
            { "SI", "CC.D.SI.USS.IP" },                 // Silver Futures
            { "PL", "CC.D.PL.USS.IP" },                 // Platinum Futures
            { "PA", "CC.D.PA.USS.IP" },                 // Palladium Futures

            // Base Metals
            { "HG", "CC.D.HG.USS.IP" },                 // Copper Futures
            { "ALI", "CC.D.ALI.USS.IP" },               // Aluminum
            { "ZINC", "CC.D.ZINC.USS.IP" },             // Zinc
            { "LEAD", "CC.D.LEAD.USS.IP" },             // Lead
            { "NICKEL", "CC.D.NICKEL.USS.IP" },         // Nickel

            // Energy
            { "CL", "CC.D.CL.USS.IP" },                 // Crude Oil WTI
            { "USOIL", "CC.D.CL.USS.IP" },              // Crude Oil WTI (alias)
            { "UKOIL", "CC.D.LCO.USS.IP" },             // Brent Crude
            { "BRN", "CC.D.LCO.USS.IP" },               // Brent Crude (alias)
            { "NG", "CC.D.NG.USS.IP" },                 // Natural Gas
            { "NATGAS", "CC.D.NG.USS.IP" },             // Natural Gas (alias)
            { "HO", "CC.D.HO.USS.IP" },                 // Heating Oil
            { "RB", "CC.D.RB.USS.IP" },                 // RBOB Gasoline
            { "GASOIL", "CC.D.GASOIL.USS.IP" },         // Gasoil

            // Agriculture - Grains & Oilseeds
            { "ZC", "CC.D.C.USS.IP" },                  // Corn
            { "CORN", "CC.D.C.USS.IP" },                // Corn (alias)
            { "ZW", "CC.D.W.USS.IP" },                  // Wheat
            { "WHEAT", "CC.D.W.USS.IP" },               // Wheat (alias)
            { "ZS", "CC.D.S.USS.IP" },                  // Soybeans
            { "SOYBEAN", "CC.D.S.USS.IP" },             // Soybeans (alias)
            { "ZO", "CC.D.BO.USS.IP" },                 // Soybean Oil
            { "ZM", "CC.D.SM.USS.IP" },                 // Soybean Meal
            { "ZR", "CC.D.RR.USS.IP" },                 // Rough Rice
            { "KE", "CC.D.KE.USS.IP" },                 // KC Wheat

            // Agriculture - Softs
            { "CT", "CC.D.CT.USS.IP" },                 // Cotton
            { "COTTON", "CC.D.CT.USS.IP" },             // Cotton (alias)
            { "KC", "CC.D.KC.USS.IP" },                 // Coffee
            { "COFFEE", "CC.D.KC.USS.IP" },             // Coffee (alias)
            { "CC", "CC.D.CC.USS.IP" },                 // Cocoa
            { "COCOA", "CC.D.CC.USS.IP" },              // Cocoa (alias)
            { "SB", "CC.D.SB.USS.IP" },                 // Sugar
            { "SUGAR", "CC.D.SB.USS.IP" },              // Sugar (alias)
            { "OJ", "CC.D.OJ.USS.IP" },                 // Orange Juice

            // Agriculture - Livestock
            { "LE", "CC.D.LE.USS.IP" },                 // Live Cattle
            { "CATTLE", "CC.D.LE.USS.IP" },             // Live Cattle (alias)
            { "HE", "CC.D.LH.USS.IP" },                 // Lean Hogs
            { "HOGS", "CC.D.LH.USS.IP" },               // Lean Hogs (alias)

            // Other Commodities
            { "LBS", "CC.D.LBS.USS.IP" },               // Lumber
            { "LUMBER", "CC.D.LBS.USS.IP" },            // Lumber (alias)
        };

        // Major Equity (Stock) mappings
        private static readonly Dictionary<string, string> EquityEpicMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // US Tech Giants (FAANG+)
            { "AAPL", "IX.D.APPLE.DAILY.IP" },          // Apple Inc
            { "MSFT", "IX.D.MICROSOFT.DAILY.IP" },      // Microsoft Corporation
            { "GOOGL", "IX.D.ALPHABET.DAILY.IP" },      // Alphabet Inc (Google)
            { "GOOG", "IX.D.ALPHABET.DAILY.IP" },       // Alphabet Inc (Google) Class C
            { "AMZN", "IX.D.AMAZON.DAILY.IP" },         // Amazon.com Inc
            { "META", "IX.D.META.DAILY.IP" },           // Meta Platforms (Facebook)
            { "NVDA", "IX.D.NVIDIA.DAILY.IP" },         // NVIDIA Corporation
            { "TSLA", "IX.D.TESLA.DAILY.IP" },          // Tesla Inc
            { "NFLX", "IX.D.NETFLIX.DAILY.IP" },        // Netflix Inc
            { "AMD", "IX.D.AMD.DAILY.IP" },             // Advanced Micro Devices

            // Other Major US Stocks
            { "JPM", "IX.D.JPM.DAILY.IP" },             // JPMorgan Chase
            { "JNJ", "IX.D.JNJ.DAILY.IP" },             // Johnson & Johnson
            { "V", "IX.D.VISA.DAILY.IP" },              // Visa Inc
            { "WMT", "IX.D.WALMART.DAILY.IP" },         // Walmart Inc
            { "PG", "IX.D.PG.DAILY.IP" },               // Procter & Gamble
            { "DIS", "IX.D.DISNEY.DAILY.IP" },          // Walt Disney Company
            { "MA", "IX.D.MASTERCARD.DAILY.IP" },       // Mastercard Inc
            { "BAC", "IX.D.BAC.DAILY.IP" },             // Bank of America
            { "XOM", "IX.D.EXXON.DAILY.IP" },           // Exxon Mobil
            { "CVX", "IX.D.CHEVRON.DAILY.IP" },         // Chevron Corporation
            { "KO", "IX.D.COCACOLA.DAILY.IP" },         // Coca-Cola Company
            { "PEP", "IX.D.PEPSI.DAILY.IP" },           // PepsiCo Inc
            { "INTC", "IX.D.INTEL.DAILY.IP" },          // Intel Corporation
            { "CSCO", "IX.D.CISCO.DAILY.IP" },          // Cisco Systems
            { "ORCL", "IX.D.ORACLE.DAILY.IP" },         // Oracle Corporation
            { "IBM", "IX.D.IBM.DAILY.IP" },             // IBM
            { "GS", "IX.D.GS.DAILY.IP" },               // Goldman Sachs
            { "MS", "IX.D.MS.DAILY.IP" },               // Morgan Stanley
            { "BA", "IX.D.BOEING.DAILY.IP" },           // Boeing Company
            { "BABA", "IX.D.ALIBABA.DAILY.IP" },        // Alibaba Group

            // UK Stocks
            { "BP", "IX.D.BP.DAILY.IP" },               // BP plc
            { "HSBA", "IX.D.HSBC.DAILY.IP" },           // HSBC Holdings
            { "LLOY", "IX.D.LLOYDS.DAILY.IP" },         // Lloyds Banking Group
            { "BARC", "IX.D.BARCLAYS.DAILY.IP" },       // Barclays plc
            { "VOD", "IX.D.VODAFONE.DAILY.IP" },        // Vodafone Group
            { "RIO", "IX.D.RIO.DAILY.IP" },             // Rio Tinto
            { "BHP", "IX.D.BHP.DAILY.IP" },             // BHP Group
            { "GSK", "IX.D.GSK.DAILY.IP" },             // GSK plc
            { "AZN", "IX.D.ASTRAZENECA.DAILY.IP" },     // AstraZeneca plc
            { "ULVR", "IX.D.UNILEVER.DAILY.IP" },       // Unilever plc
            { "DGE", "IX.D.DIAGEO.DAILY.IP" },          // Diageo plc

            // European Stocks
            { "SAP", "IX.D.SAP.DAILY.IP" },             // SAP SE (Germany)
            { "SIE", "IX.D.SIEMENS.DAILY.IP" },         // Siemens AG (Germany)
            { "ASML", "IX.D.ASML.DAILY.IP" },           // ASML Holding (Netherlands)
            { "TOT", "IX.D.TOTAL.DAILY.IP" },           // TotalEnergies (France)
            { "OR", "IX.D.LOREAL.DAILY.IP" },           // L'Oréal (France)
            { "MC", "IX.D.LVMH.DAILY.IP" },             // LVMH (France)
            { "SAN", "IX.D.SANTANDER.DAILY.IP" },       // Banco Santander (Spain)
            { "NVO", "IX.D.NOVONORDISK.DAILY.IP" },     // Novo Nordisk (Denmark)
            { "NESN", "IX.D.NESTLE.DAILY.IP" },         // Nestlé (Switzerland)
            { "NOVN", "IX.D.NOVARTIS.DAILY.IP" },       // Novartis (Switzerland)
            { "ROG", "IX.D.ROCHE.DAILY.IP" },           // Roche (Switzerland)
        };

        // Reverse mapping from EPIC to symbol info
        private static readonly Dictionary<string, (string Symbol, SecurityType SecurityType)> EpicToSymbolMap;

        static IGSymbolMapper()
        {
            EpicToSymbolMap = new Dictionary<string, (string, SecurityType)>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in ForexEpicMap)
            {
                EpicToSymbolMap[kvp.Value] = (kvp.Key, SecurityType.Forex);
            }
            foreach (var kvp in IndexEpicMap)
            {
                EpicToSymbolMap[kvp.Value] = (kvp.Key, SecurityType.Index);
            }
            foreach (var kvp in CryptoEpicMap)
            {
                EpicToSymbolMap[kvp.Value] = (kvp.Key, SecurityType.Crypto);
            }
            foreach (var kvp in CommodityEpicMap)
            {
                EpicToSymbolMap[kvp.Value] = (kvp.Key, SecurityType.Cfd);
            }
            foreach (var kvp in EquityEpicMap)
            {
                EpicToSymbolMap[kvp.Value] = (kvp.Key, SecurityType.Equity);
            }
        }

        /// <summary>
        /// Converts a LEAN symbol to an IG EPIC code
        /// </summary>
        /// <param name="symbol">The LEAN symbol</param>
        /// <returns>The IG EPIC code</returns>
        public string GetBrokerageSymbol(Symbol symbol)
        {
            if (symbol == null)
            {
                throw new ArgumentNullException(nameof(symbol));
            }

            var ticker = symbol.Value;

            switch (symbol.SecurityType)
            {
                case SecurityType.Forex:
                    if (ForexEpicMap.TryGetValue(ticker, out var forexEpic))
                    {
                        return forexEpic;
                    }
                    // Try to construct EPIC for unknown forex pairs
                    return $"CS.D.{ticker}.MINI.IP";

                case SecurityType.Index:
                    if (IndexEpicMap.TryGetValue(ticker, out var indexEpic))
                    {
                        return indexEpic;
                    }
                    break;

                case SecurityType.Crypto:
                    if (CryptoEpicMap.TryGetValue(ticker, out var cryptoEpic))
                    {
                        return cryptoEpic;
                    }
                    // Try to construct EPIC for unknown crypto
                    return $"CS.D.{ticker}.CFD.IP";

                case SecurityType.Cfd:
                    if (CommodityEpicMap.TryGetValue(ticker, out var commodityEpic))
                    {
                        return commodityEpic;
                    }
                    break;

                case SecurityType.Equity:
                    if (EquityEpicMap.TryGetValue(ticker, out var equityEpic))
                    {
                        return equityEpic;
                    }
                    break;
            }

            // Return null if no mapping found - caller should handle via API lookup
            return null;
        }

        /// <summary>
        /// Converts an IG EPIC code to a LEAN symbol
        /// </summary>
        /// <param name="brokerageSymbol">The IG EPIC code</param>
        /// <param name="securityType">The security type</param>
        /// <param name="market">The market</param>
        /// <param name="expirationDate">The expiration date (for futures/options)</param>
        /// <param name="strike">The strike price (for options)</param>
        /// <param name="optionRight">The option right (for options)</param>
        /// <returns>The LEAN symbol</returns>
        public Symbol GetLeanSymbol(
            string brokerageSymbol,
            SecurityType securityType,
            string market,
            DateTime expirationDate = default,
            decimal strike = 0,
            OptionRight optionRight = OptionRight.Call)
        {
            if (string.IsNullOrEmpty(brokerageSymbol))
            {
                throw new ArgumentNullException(nameof(brokerageSymbol));
            }

            // Try to find in reverse mapping
            if (EpicToSymbolMap.TryGetValue(brokerageSymbol, out var symbolInfo))
            {
                return Symbol.Create(symbolInfo.Symbol, symbolInfo.SecurityType, market);
            }

            // Try to parse EPIC code
            // Format: PREFIX.D.INSTRUMENT.CONTRACT.IP
            var parts = brokerageSymbol.Split('.');
            if (parts.Length >= 3)
            {
                var prefix = parts[0];
                var instrument = parts.Length >= 4 ? parts[2] : parts[1];

                // Determine security type from prefix
                var inferredSecurityType = securityType;
                if (securityType == SecurityType.Base)
                {
                    inferredSecurityType = prefix switch
                    {
                        "CS" when instrument.Contains("USD") && instrument.Length == 6 => SecurityType.Forex,
                        "CS" when instrument.Contains("BITCOIN") || instrument.Contains("ETH") => SecurityType.Crypto,
                        "IX" => SecurityType.Index,
                        "CC" => SecurityType.Cfd,
                        _ => SecurityType.Cfd
                    };
                }

                return Symbol.Create(instrument, inferredSecurityType, market);
            }

            // Fallback: create symbol directly from EPIC
            return Symbol.Create(brokerageSymbol, securityType, market);
        }

        /// <summary>
        /// Adds or updates a custom EPIC mapping
        /// </summary>
        /// <param name="leanSymbol">The LEAN symbol ticker</param>
        /// <param name="epic">The IG EPIC code</param>
        /// <param name="securityType">The security type</param>
        public void AddMapping(string leanSymbol, string epic, SecurityType securityType)
        {
            switch (securityType)
            {
                case SecurityType.Forex:
                    ForexEpicMap[leanSymbol] = epic;
                    break;
                case SecurityType.Index:
                    IndexEpicMap[leanSymbol] = epic;
                    break;
                case SecurityType.Crypto:
                    CryptoEpicMap[leanSymbol] = epic;
                    break;
                case SecurityType.Equity:
                    EquityEpicMap[leanSymbol] = epic;
                    break;
                case SecurityType.Cfd:
                    CommodityEpicMap[leanSymbol] = epic;
                    break;
                default:
                    CommodityEpicMap[leanSymbol] = epic;
                    break;
            }

            EpicToSymbolMap[epic] = (leanSymbol, securityType);
        }
    }
}
