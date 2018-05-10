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
 *
*/

using System;
using System.Collections.Generic;
using System.Linq;
using DynamicInterop;
using QuantConnect.Data;
using QuantConnect.Data.UniverseSelection;
using QuantConnect.Orders;
using QuantConnect.Securities;
using QuantConnect.Securities.Option;

namespace QuantConnect.Algorithm.CSharp
{
    /// <summary>
    /// Regression algorithm for adding and removing option chains
    /// </summary>
    public class AddRemoveOptionChainRegressionAlgorithm : QCAlgorithm
    {
        private Option TwxOptionChain;
        private Option AaplOptionChain;
        private List<Security> TwxOptionContracts = new List<Security>();
        private List<Security> AaplOptionContracts = new List<Security>();

        private bool removedTwx = false;
        private bool removedAapl = false;

        public override void Initialize()
        {
            SetStartDate(2014, 06, 05);
            SetEndDate(2014, 06, 09);
            SetCash(100000);

            TwxOptionChain = AddOption("TWX");
            TwxOptionChain.SetFilter(filter =>
            {
                return filter
                    // limit options contracts to a maximum of 180 days in the future
                    .Expiration(TimeSpan.Zero, TimeSpan.FromDays(180))
                    .Contracts(contracts =>
                    {
                        // select the latest expiring, closest to the money put contract
                        return contracts.Where(x => x.ID.OptionRight == OptionRight.Put)
                            .OrderByDescending(x => x.ID.Date)
                            .ThenBy(x => Math.Abs(filter.Underlying.Price - x.ID.StrikePrice))
                            .Take(1);
                    })
                    // forces the filter to execute only at market open and maintain the same
                    // list of contracts for the entire trading day.
                    .OnlyApplyFilterAtMarketOpen();
            });

            // add AAPL options at midnight 06.06.2014
            Schedule.On(DateRules.On(2014, 06, 06), TimeRules.At(TimeSpan.Zero), () =>
            {
                AaplOptionChain = AddOption("AAPL");
                AaplOptionChain.SetFilter(filter =>
                {
                    return filter
                        // limit options contracts to a maximum of 180 days in the future
                        .Expiration(TimeSpan.Zero, TimeSpan.FromDays(180))
                        .Contracts(contracts =>
                        {
                            // select the latest expiring, closest to the money put contract
                            return contracts.Where(x => x.ID.OptionRight == OptionRight.Put)
                                .OrderByDescending(x => x.ID.Date)
                                .ThenBy(x => Math.Abs(filter.Underlying.Price - x.ID.StrikePrice))
                                .Take(1);
                        })
                        // forces the filter to execute only at market open and maintain the same
                        // list of contracts for the entire trading day.
                        .OnlyApplyFilterAtMarketOpen();
                });
            });
        }

        public override void OnData(Slice data)
        {
            // submit orders at start of day
            if (Time.TimeOfDay.Hours == 9 && Time.TimeOfDay.Minutes == 31)
            {
                var hasTwx = data.Any(kvp => TwxOptionContracts.Any(oc => oc.Symbol.Underlying == kvp.Key.Underlying));
                var hasAapl = data.Any(kvp => AaplOptionContracts.Any(oc => oc.Symbol.Underlying == kvp.Key.Underlying));
                Log($">>{Time:o}:: SLICE SYMBOLS>> " + string.Join(", ", data.Keys));
                Log($">>{Time:o}:: SLICE SYMBOLS>> HAS TWX: {hasTwx}  HAS AAPL: {hasAapl}");

                var twxOptionContract = Securities.FirstOrDefault(kvp => !kvp.Key.IsCanonical() && kvp.Key.Underlying == TwxOptionChain.Symbol.Underlying);
                if (twxOptionContract.Value.IsTradable)
                {
                    Log($">>{Time:o}:: Order TWX>>");
                    MarketOrder(twxOptionContract.Key, 1);
                }

                if (AaplOptionChain != null)
                {
                    var aaplOptionContract = Securities.FirstOrDefault(kvp => !kvp.Key.IsCanonical() && kvp.Key.Underlying == AaplOptionChain.Symbol.Underlying);
                    if (aaplOptionContract.Key != null && data.ContainsKey(aaplOptionContract.Key))
                    {
                        Log($"{Time:o}:: Order AAPL");
                        MarketOrder(aaplOptionContract.Key, 1);
                    }
                }
            }

            // EOD 06.05.2014 remove TWX
            if (!removedTwx && Time.Date == new DateTime(2014, 06, 05) && Time.TimeOfDay.Hours == 16)
            {
                Log($">>{Time:o}:: Remove TWX Chain");
                RemoveSecurity(TwxOptionChain.Symbol);
                removedTwx = true;
            }

            if (!removedAapl && Time.Date == new DateTime(2014, 06, 09))
            {
                Log($">>{Time:o}:: Remove AAPL Chain");
                RemoveSecurity(AaplOptionChain.Symbol);
                removedAapl = true;
            }
        }

        public override void OnOrderEvent(OrderEvent fill)
        {
            Log($"{Time:o}:: {fill}");
        }

        public override void OnSecuritiesChanged(SecurityChanges changes)
        {
            Log($"{Time:o}:: {changes}");
            foreach (var added in changes.AddedSecurities)
            {
                if (added.Symbol.HasUnderlying)
                {
                    if (added.Symbol.Underlying == TwxOptionChain.Symbol.Underlying)
                    {
                        TwxOptionContracts.Add(added);
                    }
                    else if (added.Symbol.Underlying == AaplOptionChain.Symbol.Underlying)
                    {
                        AaplOptionContracts.Add(added);
                    }
                }
            }
            foreach (var removed in changes.RemovedSecurities)
            {
                if (removed.Symbol.HasUnderlying)
                {
                    if (removed.Symbol.Underlying == TwxOptionChain.Symbol.Underlying)
                    {
                        TwxOptionContracts.Remove(removed);
                    }
                    else if (removed.Symbol.Underlying == AaplOptionChain.Symbol.Underlying)
                    {
                        AaplOptionContracts.Remove(removed);
                    }
                }
            }

            // assert expected securities
            if (Time.Date == new DateTime(2014, 06, 05))
            {
                var expected = QuantConnect.Symbol.CreateOption("TWX", Market.USA, OptionStyle.American, OptionRight.Put, 70m, new DateTime(2014,10, 08));
                if (TwxOptionContracts.Count != 1 && TwxOptionContracts[0].Symbol != expected)
                {
                    throw new Exception("Regression test failed. Unexpected security selected");
                }
            }
        }
    }
}