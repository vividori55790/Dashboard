// Runs the exported dashboard's own script against real packets from a running host.
//
// Usage:
//   TelemetryDashboard.Host.exe --simulate --port 8099 --export-dashboard dash.html
//   node verify_dashboard.js dash.html 8099
//
// Why a harness rather than a browser: the page's claims are what matter, and two of them used to
// be false -- a card without data showed a number, and the connection chip asserted a connection
// no socket had made. Both are checkable without rendering anything, and checking them here means
// they are checked against the script inside the exported file rather than against a copy.
//
// What this cannot tell you: whether the page looks right. Layout, colour and legibility still
// need eyes on a browser.
const fs = require('fs');
const http = require('http');

const [, , file, portArg] = process.argv;
const port = Number(portArg || 8099);
const html = fs.readFileSync(file, 'utf8');

// ---- minimal DOM ----------------------------------------------------------
// innerHTML is not parsed; ids are extracted from it and registered as stubs. That is enough,
// because everything the script does afterwards goes through getElementById.
const elements = new Map();

function makeEl(id) {
    const el = {
        id,
        innerText: '',
        _innerHTML: '',
        style: {},
        className: '',
        children: [],
        appendChild(c) { this.children.push(c); },
        getContext: () => ({
            clearRect() {}, beginPath() {}, moveTo() {}, lineTo() {}, stroke() {},
            set strokeStyle(v) {}, set lineWidth(v) {}
        }),
        get parentElement() { return { clientWidth: 400 }; },
        get innerHTML() { return this._innerHTML; },
        set innerHTML(v) {
            this._innerHTML = v;
            for (const m of String(v).matchAll(/id="([^"]+)"/g)) {
                if (!elements.has(m[1])) elements.set(m[1], makeEl(m[1]));
            }
        }
    };
    return el;
}

const listeners = {};
const document = {
    getElementById: id => elements.get(id) || null,
    createElement: () => makeEl(''),
    querySelector: sel => elements.get(sel) || (elements.set(sel, makeEl(sel)), elements.get(sel)),
    addEventListener: (name, fn) => { (listeners[name] ||= []).push(fn); }
};
elements.set('dashboard-container', makeEl('dashboard-container'));
elements.set('conn-status', makeEl('conn-status'));

const window = { addEventListener: document.addEventListener };

// ---- TelemetryClient stub -------------------------------------------------
let dataCb = null, statusCb = null, connectedTo = null;
const TelemetryClient = {
    connect(url) { connectedTo = url; return this; },
    onData(cb) { dataCb = cb; return this; },
    onStatusChange(cb) { statusCb = cb; cb('DISCONNECTED', false); return this; }
};

// ---- run the page's own script -------------------------------------------
const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
if (scripts.length === 0) { console.error('FAIL: no inline script in the exported page'); process.exit(1); }

const vm = require('vm');
const sandbox = { document, window, TelemetryClient, console };
vm.createContext(sandbox);
// widgetConfigs is a top-level const, which vm keeps in the script's lexical scope rather than on
// the sandbox object, so it is re-exported here. The script itself is untouched.
vm.runInContext(
    scripts[scripts.length - 1] + '\n;globalThis.__widgets = widgetConfigs;', sandbox);
(listeners['DOMContentLoaded'] || []).forEach(fn => fn());

const widgets = sandbox.__widgets;
let failures = 0;
function check(name, ok, detail) {
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -- ' + detail : ''}`);
    if (!ok) failures++;
}

check('the page connects to the port it was exported for',
    connectedTo === `ws://localhost:${port}/ws`, `connected to ${connectedTo}`);
check('widgets were built from a profile', widgets.length > 0, `${widgets.length} widgets`);

// Before any packet: every value reads as unknown, not as zero.
//
// Read out of the markup the page built rather than out of the stub elements. The shim registers
// an id when it sees one inside an innerHTML string but does not parse that string into a tree,
// so a stub's own innerHTML is empty where a browser would show the placeholder. Asserting on the
// stub would have been asserting on the harness.
const builtMarkup = elements.get('dashboard-container').children
    .map(c => c.innerHTML).join('\n');
// Widget ids are slugged to letters, digits and hyphens by the exporter, so they need no escaping.
const placeholders = widgets.filter(w => builtMarkup.includes(`id="val-${w.Id}"`)
    && new RegExp(`id="val-${w.Id}"[^>]*>--`).test(builtMarkup));
check('no card shows a number before data arrives',
    placeholders.length === widgets.length,
    `${placeholders.length} of ${widgets.length} cards start at the placeholder`);

check('the connection chip does not claim a connection at rest',
    !/연결됨|CONNECTED/.test(document.getElementById('conn-status').innerText),
    JSON.stringify(document.getElementById('conn-status').innerText));

statusCb('CONNECTED', true);
check('the chip reports a connection once the socket opens',
    /연결됨/.test(document.getElementById('conn-status').innerText),
    JSON.stringify(document.getElementById('conn-status').innerText));

// ---- feed it real packets from the running host ---------------------------
const seen = new Set();
const req = http.get({ host: 'localhost', port, path: '/stream' }, res => {
    let buf = '';
    res.on('data', chunk => {
        buf += chunk.toString();
        let idx;
        while ((idx = buf.indexOf('\n\n')) >= 0) {
            const line = buf.slice(0, idx).trim();
            buf = buf.slice(idx + 2);
            if (!line.startsWith('data: ')) continue;
            let pkt;
            try { pkt = JSON.parse(line.slice(6)); } catch { continue; }
            if (typeof pkt.variable !== 'string') continue;
            seen.add(pkt.variable);
            dataCb(pkt);
        }
        if (seen.size >= widgets.length / 2) { req.destroy(); finish(); }
    });
});
req.on('error', e => { console.error('FAIL: could not read the host stream:', e.message); process.exit(1); });
setTimeout(() => { req.destroy(); finish(); }, 15000);

let finished = false;
function finish() {
    if (finished) return;
    finished = true;

    const fed = widgets.filter(w => seen.has(w.Field));
    const starved = widgets.filter(w => !seen.has(w.Field));

    check('every channel the host sent reached its card',
        fed.length > 0 && fed.every(w => {
            const v = document.getElementById(`val-${w.Id}`).innerHTML;
            return v && !v.startsWith('--');
        }),
        `${fed.length} of ${widgets.length} cards fed by ${seen.size} channel(s)`);

    check('a card whose channel never reported still shows no value',
        starved.every(w => document.getElementById(`val-${w.Id}`).innerHTML.startsWith('--')),
        `${starved.length} starved card(s)`);

    // The defect this replaces: a missing field fell back to data.temp and then to 0, so a card
    // could show another quantity's reading under its own heading.
    const bogus = { variable: 'a.channel.no.widget.asked.for', value: 999.9, unit: 'x' };
    const snapshot = widgets.map(w => document.getElementById(`val-${w.Id}`).innerHTML);
    dataCb(bogus);
    const after = widgets.map(w => document.getElementById(`val-${w.Id}`).innerHTML);
    check('an unrelated channel changes no card',
        JSON.stringify(snapshot) === JSON.stringify(after),
        'a packet for an unknown channel must not be shown under someone else’s label');

    console.log(`\n${failures === 0 ? 'all checks passed' : failures + ' check(s) failed'}`);
    process.exit(failures === 0 ? 0 : 1);
}
