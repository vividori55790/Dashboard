using System;
using System.Collections.Generic;

namespace TelemetryDashboard.Core.Backtesting;

/// <summary>
/// Walks a series one bar at a time, asking a strategy what to hold and filling it on the next open.
/// </summary>
/// <remarks>
/// The one rule that decides whether any of this is worth reading: a decision made from a bar's
/// close is filled at the <em>next</em> bar's open. A backtest that fills at the close it decided
/// from is buying at a price it could only know after the market stopped offering it, and the
/// result is not a slightly optimistic estimate — it is a measurement of a machine that can see the
/// future. On a decade of daily bars that single line is worth more than every other modelling
/// choice here combined.
/// <para>
/// The consequence is deliberate and is reported: the final bar's decision is never executed,
/// because there is no session after it to execute in.
/// </para>
/// <para>
/// What this does not model, and what a result from it therefore cannot be used to claim: the
/// borrow cost and margin requirement of a short, dividends other than through an adjusted close,
/// taxes, position limits, and the possibility that an order large enough to matter moves the price
/// it is filling against. Slippage here is a fixed rate, not a function of size.
/// </para>
/// </remarks>
public sealed class BacktestEngine
{
    private readonly BacktestSettings _settings;
    private readonly PositionLedger _position = new();
    private readonly RoundTripTracker _trips = new();
    private readonly List<TradeFill> _fills = new();
    private readonly List<EquityPoint> _curve = new();
    private double _cash;
    private double _commissionPaid;
    private double _slippagePaid;

    /// <summary>Builds an engine that runs under <paramref name="settings"/>.</summary>
    /// <exception cref="ArgumentException">Thrown when the settings cannot describe a run.</exception>
    public BacktestEngine(BacktestSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Validate() is { } problem) throw new ArgumentException(problem, nameof(settings));

        _settings = settings;
    }

    /// <summary>Runs <paramref name="strategy"/> over <paramref name="series"/>.</summary>
    public BacktestResult Run(BarSeries series, IBarStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(series);
        ArgumentNullException.ThrowIfNull(strategy);

        StartFresh(strategy);
        double? pending = null;
        int barsHolding = 0;

        for (int index = 0; index < series.Count; index++)
        {
            PriceBar bar = series[index];

            // Yesterday's decision, filled at today's open -- before anything is read from today.
            if (pending is { } target)
            {
                Execute(target, bar.Date, bar.OpenOf(_settings.Field));
                pending = null;
            }

            double price = bar.PriceOf(_settings.Field);
            double equity = _cash + _position.MarketValue(price);
            double weight = equity == 0 ? 0 : _position.MarketValue(price) / equity;
            if (_position.Shares != 0) barsHolding++;

            _curve.Add(new EquityPoint { Date = bar.Date, Equity = equity, Weight = weight, Price = price });

            // Last, so a strategy is never asked about a bar whose fill has not been applied.
            pending = strategy.Decide(new StrategyContext(series, index, _settings.Field, weight));
        }

        return new BacktestResult
        {
            Symbol = series.Symbol,
            StrategyName = strategy.Name,
            Settings = _settings,
            // All three are copied out. The first version of this handed over the tracker's live
            // list while copying the other two, and the second run on this engine cleared it -- so
            // a report printed nine fills and zero round trips beside them, and the win rate that
            // depends on those trips read "n/a" for a rule that had opened and closed four times.
            // Found by running it, not by a test, because every test held one result at a time.
            Curve = _curve.ToArray(),
            Fills = _fills.ToArray(),
            RoundTrips = _trips.Closed.ToArray(),
            WarmUpBars = Math.Min(strategy.WarmUpBars, series.Count),
            BarsHoldingPosition = barsHolding,
            CommissionPaid = _commissionPaid,
            SlippagePaid = _slippagePaid,
            EndedWithOpenPosition = _trips.HasOpenTrip,
            UnexecutedFinalSignal = pending
        };
    }

    private void StartFresh(IBarStrategy strategy)
    {
        _position.Reset();
        _trips.Reset();
        _fills.Clear();
        _curve.Clear();
        _cash = _settings.StartingCash;
        _commissionPaid = 0;
        _slippagePaid = 0;
        strategy.Reset();
    }

    /// <summary>Moves the position towards <paramref name="targetWeight"/> at <paramref name="referencePrice"/>.</summary>
    private void Execute(double targetWeight, DateOnly date, double referencePrice)
    {
        if (!double.IsFinite(referencePrice) || referencePrice <= 0) return;

        double equity = _cash + _position.MarketValue(referencePrice);

        // An account wiped out by a leveraged short cannot buy its way back, and letting the
        // arithmetic continue past zero produces a negative-equity curve that every ratio below
        // would happily divide by.
        if (!double.IsFinite(equity) || equity <= 0) return;

        double delta = targetWeight * equity / referencePrice - _position.Shares;
        if (Math.Abs(delta) * referencePrice < _settings.MinimumTradeFraction * equity) return;

        double fillPrice = referencePrice * (1 + _settings.SlippageRate * Math.Sign(delta));
        double traded = Math.Abs(delta) * fillPrice;
        double commission = traded * _settings.CommissionRate;
        double slippage = Math.Abs(delta) * Math.Abs(fillPrice - referencePrice);

        double before = _position.Shares;
        double realised = _position.Apply(delta, fillPrice);
        _cash -= delta * fillPrice + commission;
        _commissionPaid += commission;
        _slippagePaid += slippage;

        _trips.Record(date, before, _position.Shares, realised, commission + slippage);
        _fills.Add(new TradeFill
        {
            Date = date,
            Shares = delta,
            Price = fillPrice,
            ReferencePrice = referencePrice,
            Commission = commission,
            RealisedProfit = realised,
            EquityAfter = _cash + _position.MarketValue(referencePrice)
        });
    }
}
