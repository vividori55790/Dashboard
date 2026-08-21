// Runs dab_psfb_console.html's own script against real packets from a running host.
//
// Usage:
//   TelemetryDashboard.Host.exe --simulate --profile dab-psfb-ups --port 8099 //     --computed "psfb.efficiency[%] = 100 * psfb.output_voltage * psfb.output_current / (dab.bus_voltage * dab.input_current)" //     --computed "sensor.missing = nowhere.at_all * 2"
//   node verify_dab_console.js dab_psfb_console.html 8099
//
// Both --computed flags are needed by checks below: the first gives three rows that must agree
// with each other, the second gives a row the host will refuse to compute, so the check that an
// unavailable channel prints no number has something to find. Without them those checks fail and
// say which flag is missing, rather than passing over an empty set.
//
// The claim being checked is narrow and was false in the page this replaces: that each channel's
// reading lands in its own card. power_ups_psfb_dashboard.html matched variable names by substring,
// so includes('voltage') caught grid.voltage, dab.bus_voltage and psfb.output_voltage alike and
// wrote all three into the DAB bus field -- three measurements from three points in the power chain
// overwriting each other in one box, several times a second.
//
// What this cannot tell you: whether the page looks right. Layout and legibility need a browser.
const fs = require('fs');
const http = require('http');
const vm = require('vm');

const CELL = /<td[^>]*>([^<]*)<\/td>/g;
const UNAVAILABLE = /class="unavailable"/;
const READING = /읽는 중/;
const DASH = '—';

const [, , file, portArg] = process.argv;
const port = Number(portArg || 8099);
const html = fs.readFileSync(file, 'utf8');

// ---- minimal DOM ----------------------------------------------------------
// innerHTML is stored, not parsed; ids inside it are registered as stubs, which is enough because
// everything afterwards goes through getElementById.
const elements = new Map();

function makeEl(id) {
    const el = {
        _id: id, innerText: '', textContent: '', className: '', _innerHTML: '',
        style: {}, children: [], clientWidth: 400,
        appendChild(c) { this.children.push(c); return c; },
        insertBefore(c) { this.children.unshift(c); return c; },
        removeChild(c) { this.children = this.children.filter(x => x !== c); return c; },
        querySelector: () => null,
        get firstChild() { return this.children[0] || null; },
        get lastChild() { return this.children[this.children.length - 1] || null; },
        classList: {
            _s: new Set(),
            add(...n) { n.forEach(x => this._s.add(x)); },
            remove(...n) { n.forEach(x => this._s.delete(x)); },
            toggle(n, on) { on ? this._s.add(n) : this._s.delete(n); },
            contains(n) { return this._s.has(n); }
        },
        getContext: () => ({
            clearRect() {}, beginPath() {}, moveTo() {}, lineTo() {}, stroke() {},
            set strokeStyle(v) {}, set lineWidth(v) {}
        }),
        get parentElement() { return { clientWidth: 400 }; },
        get innerHTML() { return this._innerHTML; },
        set innerHTML(v) {
            this._innerHTML = String(v);
            // Register ids that appear inside the markup, and remember the text each one was
            // given, so a stub can answer for its own placeholder. Without this the harness
            // reports an empty string where a browser shows the em dash -- an assertion about
            // the shim rather than about the page.
            for (const m of this._innerHTML.matchAll(/id="([^"]+)"[^>]*>([^<]*)/g)) {
                const child = elements.get(m[1]) || makeEl(m[1]);
                child._innerHTML = m[2];
                child.textContent = m[2];
                elements.set(m[1], child);
            }
        },
        // The page assigns element.id directly on created nodes; registering on assignment is
        // what makes getElementById find them afterwards.
        get id() { return this._id; },
        set id(v) { this._id = v; elements.set(v, this); }
    };
    el.classList = Object.create(el.classList);
    el.classList._s = new Set();
    return el;
}

['chain', 'conn-dot', 'conn-text', 'node-chip', 'sim-banner', 'log', 'table',
 'derived-panel', 'derived-table']
    .forEach(id => elements.set(id, makeEl(id)));

const tbody = makeEl('tbody');
const derivedBody = makeEl('derived-tbody');
elements.get('table').querySelector = () => tbody;

const document = {
    getElementById: id => elements.get(id) || null,
    createElement: () => makeEl(''),
    querySelector: sel => {
        if (sel === '#table tbody') return tbody;
        if (sel === '#derived-table tbody') return derivedBody;
        return elements.get(sel) || makeEl(sel);
    },
    addEventListener: () => {}
};

// A real request to the running host, not a canned reply. The point of this harness is that the
// page is exercised against what the host actually serves; a stubbed /api/computed would only
// confirm that the page can render a shape this file invented.
const pendingFetches = [];
function sandboxFetch(path) {
    const p = new Promise((resolve, reject) => {
        http.get({ host: 'localhost', port, path }, res => {
            let body = '';
            res.on('data', c => { body += c; });
            res.on('end', () => resolve({ json: () => Promise.resolve(JSON.parse(body)) }));
        }).on('error', reject);
    });
    pendingFetches.push(p.catch(() => null));
    return p;
}

let dataCb = null, statusCb = null, connectedTo = null;
const TelemetryClient = {
    connect(url) { connectedTo = url; return this; },
    onData(cb) { dataCb = cb; return this; },
    onStatusChange(cb) { statusCb = cb; cb('DISCONNECTED', false); return this; }
};

const sandbox = {
    document,
    console,
    TelemetryClient,
    setInterval: () => 0,
    fetch: sandboxFetch,
    Promise,
    location: { search: '', port: String(port), hostname: 'localhost' },
    URLSearchParams: class { get() { return null; } },
    Date, Math, Number, String, Array, Object, JSON
};

const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
if (!scripts.length) { console.error('FAIL: no inline script'); process.exit(1); }

vm.createContext(sandbox);
vm.runInContext(scripts[scripts.length - 1], sandbox);

let failures = 0;
function check(name, ok, detail) {
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -- ' + detail : ''}`);
    if (!ok) failures++;
}

const STAGES = ['grid.voltage', 'dab.bus_voltage', 'psfb.output_voltage',
                'dab.input_current', 'psfb.output_current', 'server.load'];
const key = id => id.replace(/[^a-zA-Z0-9]/g, '-');
const valueOf = id => document.getElementById('v-' + key(id))?.innerHTML ?? '(missing)';

check('the page connects to the port it was served from',
    connectedTo === `ws://localhost:${port}/ws`, connectedTo);

check('a card exists for every channel in the chain',
    STAGES.every(id => document.getElementById('v-' + key(id))), STAGES.join(', '));

check('no card shows a number before data arrives',
    STAGES.every(id => valueOf(id) === '—'), STAGES.map(valueOf).join(' | '));

check('the connection chip does not claim a connection at rest',
    !/연결됨/.test(document.getElementById('conn-text').textContent),
    JSON.stringify(document.getElementById('conn-text').textContent));

check('the simulated banner is hidden until a simulated packet says otherwise',
    document.getElementById('sim-banner').style.display !== 'block');

statusCb('CONNECTED', true);
check('the chip reports a connection once the socket opens',
    /연결됨/.test(document.getElementById('conn-text').textContent),
    JSON.stringify(document.getElementById('conn-text').textContent));

// ---- real packets ---------------------------------------------------------
const seen = new Set();
const req = http.get({ host: 'localhost', port, path: '/stream' }, res => {
    let buf = '';
    res.on('data', chunk => {
        buf += chunk.toString();
        let i;
        while ((i = buf.indexOf('\n\n')) >= 0) {
            const line = buf.slice(0, i).trim();
            buf = buf.slice(i + 2);
            if (!line.startsWith('data: ')) continue;
            let pkt;
            try { pkt = JSON.parse(line.slice(6)); } catch { continue; }
            if (typeof pkt.variable !== 'string') continue;
            seen.add(pkt.variable);
            dataCb(pkt);
        }
        if (STAGES.every(s => seen.has(s))) { req.destroy(); finish(); }
    });
});
req.on('error', e => { console.error('FAIL: cannot read host stream:', e.message); process.exit(1); });
setTimeout(() => { req.destroy(); finish(); }, 20000);

let done = false;
async function finish() {
    if (done) return;
    done = true;

    check('every channel the host sent reached a card',
        STAGES.filter(s => seen.has(s)).every(s => valueOf(s) !== '—'),
        STAGES.map(s => `${s}=${valueOf(s).replace(/<[^>]*>/g, '')}`).join('  '));

    // The defect this page exists to fix: three voltages in one box.
    const numeric = id => parseFloat(String(valueOf(id)).replace(/<[^>]*>/g, ''));

    // Read before the probe below overwrites the cards.
    const dab = numeric('dab.bus_voltage');
    const psfb = numeric('psfb.output_voltage');
    const dabCurrent = numeric('dab.input_current');

    // Written first as grid !== dab !== psfb, which is a coincidence and not a property: the two
    // buses overlap (0-440 and 350-450), so two unrelated channels rounding to the same integer
    // failed this harness on a page that was working. Distinct injected values test the routing
    // itself and cannot collide.
    const PROBE = { 'grid.voltage': 111, 'dab.bus_voltage': 222, 'psfb.output_voltage': 333 };
    Object.keys(PROBE).forEach(id => dataCb({ variable: id, value: PROBE[id], unit: 'V', simulated: true }));

    check('each voltage stays in its own card',
        Object.keys(PROBE).every(id => numeric(id) === PROBE[id]),
        Object.keys(PROBE).map(id => `${id}=${numeric(id)} (sent ${PROBE[id]})`).join('  '));

    // Each value has to be inside the range its own stage declares, which is the strongest
    // available check that it is not another stage's reading wearing this stage's label.
    check('psfb.output_voltage reads as a 48 V rail, not a 400 V bus',
        psfb > 38 && psfb < 54, String(psfb));
    check('dab.bus_voltage reads as a 400 V bus',
        dab > 350 && dab < 450, String(dab));

    check('a simulated stream raises the synthetic-data banner',
        document.getElementById('sim-banner').style.display === 'block');

    const before = STAGES.map(valueOf);
    dataCb({ variable: 'not.in.this.chain', value: 999.9, unit: 'x', simulated: true });
    check('a channel outside the chain changes no card',
        JSON.stringify(before) === JSON.stringify(STAGES.map(valueOf)));

    // ---- computed channels --------------------------------------------------
    // Polled once at load. Waiting on the HTTP responses alone is not enough and was wrong here:
    // the page's own chain is fetch().then(json).then(render), so when the response settles the
    // render is still two microtask hops away. Against a warm host those hops happened to land
    // before these checks and everything passed; against a freshly started one they did not, and
    // the harness reported an empty panel that the browser would have filled. A macrotask tick
    // after the responses drains every queued continuation, which makes the wait deterministic
    // rather than lucky.
    await Promise.allSettled(pendingFetches);
    await new Promise(resolve => setImmediate(resolve));
    // className is a property assignment on the row, not part of its innerHTML, so it has to be
    // read from the element. Matching on the markup instead made the unavailable-row check below
    // pass without ever finding an unavailable row -- an assertion about this harness.
    const rows = derivedBody.children.map(c => ({
        html: String(c.innerHTML),
        className: String(c.className || '')
    }));
    const cells = r => [...r.html.matchAll(CELL)].map(m => m[1]);

    check('the derived panel rendered rows from /api/computed',
        rows.length > 0 && !rows.some(r => READING.test(r.html)),
        rows.length + ' row(s); fetches=' + pendingFetches.length + '; panel holds: ' + String(derivedBody.innerHTML).slice(0, 160));

    check('every derived row carries the expression it was computed from',
        rows.length > 0 && rows.every(r => cells(r).length === 4 && cells(r)[2].length > 0),
        rows.map(r => cells(r)[0]).join(', '));

    // The one thing this panel must never do: print a number for a channel the host refused to
    // compute. An unavailable row shows the em dash and the host's reason -- never 0, and never
    // the last value it happened to have.
    const unavailable = rows.filter(r => r.className.includes('unavailable'));
    check('an unavailable derived channel shows no number',
        unavailable.length > 0 && unavailable.every(r => cells(r)[1] === DASH),
        unavailable.length ? unavailable.map(r => cells(r)[0] + '=' + cells(r)[1]).join(', ')
                           : 'no row was unavailable, so this check found nothing to test -- '
                             + 'run the host with --computed "x = nowhere.at_all * 2"');

    // Every row in one reply is computed at the same instant, which is the property that
    // separates this endpoint from reading the latest of each channel. So the rows have to agree
    // with each other exactly: efficiency is p_out over p_in, and if the endpoint had evaluated
    // each expression at its own moment they would disagree in the last digits.
    const cellValue = id => {
        const row = rows.find(r => cells(r)[0] === id);
        return row ? parseFloat(cells(row)[1]) : NaN;
    };

    const pin = cellValue('dab.p_in');
    const pout = cellValue('psfb.p_out');
    const eff = cellValue('psfb.efficiency');

    if (!Number.isFinite(eff)) {
        // Said plainly rather than reported as a failed comparison: a NaN here means the host was
        // not started with the channel this check needs, which is a different thing from the rows
        // disagreeing.
        check('psfb.efficiency is declared, without which this check has nothing to compare',
            false,
            'rerun the host with --computed "psfb.efficiency[%] = 100 * psfb.output_voltage * '
            + 'psfb.output_current / (dab.bus_voltage * dab.input_current)"');
    } else {
        check('the derived rows are consistent with each other, so they share one instant',
            [pin, pout].every(Number.isFinite)
            && Math.abs(eff - (100 * pout / pin)) < 1e-3,
            'efficiency=' + eff + ' vs 100*p_out/p_in=' + (100 * pout / pin).toFixed(6));
    }

    console.log(`\n${failures === 0 ? 'all checks passed' : failures + ' check(s) failed'}`);
    process.exit(failures === 0 ? 0 : 1);
}
