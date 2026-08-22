# Sample price files

Three daily OHLCV files, staged beside the executable as `samples/` so
`TelemetryDashboard.Host backtest SPY` works from a fresh build with nothing else set up.

## Provenance

| File | Instrument | Sessions | Range |
|------|-----------|----------|-------|
| `SPY.csv`  | SPDR S&P 500 ETF | 2514 | 2016-08-22 .. 2026-08-21 |
| `AAPL.csv` | Apple Inc.        | 2514 | 2016-08-22 .. 2026-08-21 |
| `KO.csv`   | The Coca-Cola Company | 2514 | 2016-08-22 .. 2026-08-21 |

Retrieved 2026-08-23 from the Yahoo Finance chart endpoint
(`query1.finance.yahoo.com/v8/finance/chart/<symbol>?range=10y&interval=1d`) and written out in
Yahoo's own CSV export layout: `Date,Open,High,Low,Close,Adj Close,Volume`.

**This is real market data, not generated.** Nothing here is synthetic, interpolated or smoothed —
which is the point, because a backtester validated only against a series someone invented has been
validated against a series with no gaps, no halts and no split.

Two consequences worth stating rather than discovering:

- It is a **vendor's** data. If this repository is published, check that redistributing it is
  something you want to do; the backtester reads any file in this layout, so removing these three
  costs nothing but the one-line example.
- `Adj Close` is Yahoo's adjustment for splits and dividends. The engine defaults to it. Passing
  `--price close` uses the raw print instead, which shows a split as a fall that never happened.

## Refetching, or adding a symbol

Any daily export in this layout works, including Stooq's (which has no `Adj Close`; the reader
falls back to the close and says so in the report). To regenerate these, or add another symbol:

```bash
python - <<'PY'
import json, urllib.request, datetime
SYMBOLS = ["SPY", "AAPL", "KO"]
for sym in SYMBOLS:
    url = f"https://query1.finance.yahoo.com/v8/finance/chart/{sym}?range=10y&interval=1d"
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=30) as r:
        res = json.load(r)["chart"]["result"][0]
    q, ts = res["indicators"]["quote"][0], res["timestamp"]
    adj = res["indicators"].get("adjclose", [{}])[0].get("adjclose", [None] * len(ts))
    rows = []
    for i, t in enumerate(ts):
        o, h, l, c = q["open"][i], q["high"][i], q["low"][i], q["close"][i]
        if None in (o, h, l, c):
            continue          # a halted session; the reader would drop it anyway, and say so
        d = datetime.datetime.utcfromtimestamp(t).strftime("%Y-%m-%d")
        a = adj[i] if adj and adj[i] is not None else c
        rows.append(f"{d},{o:.6f},{h:.6f},{l:.6f},{c:.6f},{a:.6f},{int(q['volume'][i] or 0)}")
    with open(f"{sym}.csv", "w", newline="\n") as f:
        f.write("Date,Open,High,Low,Close,Adj Close,Volume\n" + "\n".join(rows) + "\n")
    print(f"{sym}: {len(rows)} bars")
PY
```

The endpoint is not a stable public API and may change or refuse a request. If it does, any
broker or data vendor's daily CSV export can be dropped into this directory under the symbol name
and used the same way.
