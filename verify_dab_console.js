// Runs dab_psfb_console.html's own script against real packets from a running host.
//
// Usage:
//   TelemetryDashboard.Host.exe --simulate --profile dab-psfb-ups --port 8099
//   node verify_dab_console.js dab_psfb_console.html 8099
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

['chain', 'conn-dot', 'conn-text', 'node-chip', 'sim-banner', 'log', 'table']
    .forEach(id => elements.set(id, makeEl(id)));

const tbody = makeEl('tbody');
elements.get('table').querySelector = () => tbody;

const document = {
    getElementById: id => elements.get(id) || null,
    createElement: () => makeEl(''),
    querySelector: sel => (sel === '#table tbody' ? tbody : (elements.get(sel) || makeEl(sel))),
    addEventListener: () => {}
};

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

const STAGES = ['grid.voltage', 'dab.bus_voltage', 'psfb.output_voltage', 'server.load'];
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
function finish() {
    if (done) return;
    done = true;

    check('every channel the host sent reached a card',
        STAGES.filter(s => seen.has(s)).every(s => valueOf(s) !== '—'),
        STAGES.map(s => `${s}=${valueOf(s).replace(/<[^>]*>/g, '')}`).join('  '));

    // The defect this page exists to fix: three voltages in one box.
    const numeric = id => parseFloat(String(valueOf(id)).replace(/<[^>]*>/g, ''));
    const grid = numeric('grid.voltage');
    const dab = numeric('dab.bus_voltage');
    const psfb = numeric('psfb.output_voltage');

    check('each voltage stays in its own card',
        Number.isFinite(grid) && Number.isFinite(dab) && Number.isFinite(psfb)
        && grid !== dab && dab !== psfb,
        `grid=${grid} dab=${dab} psfb=${psfb}`);

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

    console.log(`\n${failures === 0 ? 'all checks passed' : failures + ' check(s) failed'}`);
    process.exit(failures === 0 ? 0 : 1);
}
