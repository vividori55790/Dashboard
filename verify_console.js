// Runs stream_client.html's own script against a live host, then exercises the endpoints it calls.
//
// Usage:
//   TelemetryDashboard.Host.exe --simulate --port 8099
//   node verify_console.js stream_client.html 8099
//
// The console this replaces could not display the hub's telemetry at all: it read data.temp,
// data.vin, data.vout and seven other fields the frame does not contain, and its general mechanism
// -- fetch('/api/config') -- had no server side, so every load fell into a legacy path that
// assigned latestPSFB.vout = 48.0 to a device that had reported nothing.
//
// So the checks here are about discovery: that channels appear because they arrived, that each one
// keeps its own value, and that nothing is shown for a channel nobody sent.
//
// What this cannot tell you: whether the page looks right. That needs a browser.
const fs = require('fs');
const http = require('http');
const vm = require('vm');

const [, , file, portArg] = process.argv;
const port = Number(portArg || 8099);
const html = fs.readFileSync(file, 'utf8');

const elements = new Map();
const timers = [];

function makeEl(id) {
    const el = {
        _id: id, textContent: '', className: '', _innerHTML: '', value: '', disabled: false,
        style: {}, children: [], clientWidth: 400,
        appendChild(c) { this.children.push(c); return c; },
        addEventListener(name, fn) { (this._on ||= {})[name] = fn; },
        fire(name) { this._on?.[name]?.(); },
        getContext: () => ({
            clearRect() {}, beginPath() {}, moveTo() {}, lineTo() {}, stroke() {}, fillRect() {},
            fillText() {}, set strokeStyle(v) {}, set lineWidth(v) {}, set fillStyle(v) {},
            set font(v) {}, set textAlign(v) {}
        }),
        classList: null,
        get innerHTML() { return this._innerHTML; },
        set innerHTML(v) {
            this._innerHTML = String(v);
            for (const m of this._innerHTML.matchAll(/id="([^"]+)"[^>]*>([^<]*)/g)) {
                const child = elements.get(m[1]) || makeEl(m[1]);
                child._innerHTML = m[2];
                child.textContent = m[2];
                elements.set(m[1], child);
            }
        },
        get id() { return this._id; },
        set id(v) { this._id = v; elements.set(v, this); }
    };
    const set = new Set();
    el.classList = {
        add: (...n) => n.forEach(x => set.add(x)),
        remove: (...n) => n.forEach(x => set.delete(x)),
        toggle: (n, on) => on ? set.add(n) : set.delete(n),
        contains: n => set.has(n)
    };
    return el;
}

['grid', 'empty', 'conn-dot', 'conn-text', 'stat-chip', 'sim-banner', 'spec-ch', 'spec-win',
 'spec-go', 'spec-canvas', 'spec-stat', 'dvr-load', 'dvr-range', 'dvr-scrub', 'dvr-stat',
 'dvr-frames'].forEach(id => elements.set(id, makeEl(id)));
elements.get('spec-win').value = '60';

const document = {
    getElementById: id => elements.get(id) || null,
    createElement: () => makeEl(''),
    querySelector: sel => elements.get(sel) || makeEl(sel),
    addEventListener: () => {}
};

let dataCb = null, statusCb = null, connectedTo = null;
const TelemetryClient = {
    connect(u) { connectedTo = u; return this; },
    onData(cb) { dataCb = cb; return this; },
    onStatusChange(cb) { statusCb = cb; cb('DISCONNECTED', false); return this; }
};

const fetches = [];
const sandbox = {
    document, console, TelemetryClient,
    setInterval: (fn) => { timers.push(fn); return timers.length; },
    location: { hostname: 'localhost', port: String(port) },
    fetch: (url) => {
        fetches.push(url);
        return new Promise((resolve, reject) => {
            http.get('http://localhost:' + port + url, res => {
                let b = '';
                res.on('data', c => b += c);
                res.on('end', () => resolve({ ok: res.statusCode === 200, json: () => JSON.parse(b) }));
            }).on('error', reject);
        });
    },
    encodeURIComponent, Number, Math, Date, JSON, Object, Array, String
};

const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
vm.createContext(sandbox);
vm.runInContext(scripts[scripts.length - 1], sandbox);

let failures = 0;
function check(name, ok, detail) {
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -- ' + detail : ''}`);
    if (!ok) failures++;
}

check('the console connects to the port it was served from',
    connectedTo === `ws://localhost:${port}/ws`, connectedTo);
check('no channel is shown before any arrives',
    elements.get('grid').children.length === 0, `${elements.get('grid').children.length} cards`);
check('the chip does not claim a connection at rest',
    !/연결됨/.test(elements.get('conn-text').textContent),
    JSON.stringify(elements.get('conn-text').textContent));

statusCb('CONNECTED', true);
check('the chip follows the socket',
    /연결됨/.test(elements.get('conn-text').textContent));

// Variables that arrived marked derived, so the label check knows what to look for.
const derivedByVariable = new Set();
const breachedByVariable = new Set();
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
            let p; try { p = JSON.parse(line.slice(6)); } catch { continue; }
            if (typeof p.variable !== 'string') continue;
            if (p.derived === true) derivedByVariable.add(p.variable);
            if (p.limitBreach === true) breachedByVariable.add(p.variable);
            seen.add(p.variable);
            dataCb(p);
        }
        // No early stop. Counting channels was wrong twice over: four channels used to mean four
        // raw ones, and once derived channels existed a single tick published four of them in one
        // burst -- so the capture ended having seen nothing else, and the checks below reported a
        // console that was working as broken. A fixed window sees whatever the host actually
        // sends, in the proportions it sends it.
    });
});
req.on('error', e => { console.error('FAIL: cannot read stream:', e.message); process.exit(1); });
setTimeout(() => { req.destroy(); afterStream(); }, 6000);

let done = false;
function afterStream() {
    if (done) return;
    done = true;

    const cards = elements.get('grid').children.length;
    check('a card appeared for every channel that arrived',
        cards === seen.size, `${cards} cards for ${seen.size} channel(s): ${[...seen].join(', ')}`);

    check('the synthetic-data banner is raised for a simulated stream',
        elements.get('sim-banner').style.display === 'block');

    // Each card must hold its own reading. The console this replaces wrote one device's value
    // under another's name whenever only one had reported.
    const values = [...seen].map(v => {
        const el = [...elements.entries()].find(([k]) => k.startsWith('v-c') && k.includes(v.replace(/[^a-zA-Z0-9]/g, '-')));
        return el ? el[1].innerHTML : null;
    }).filter(Boolean);

    check('every card carries a reading of its own',
        values.length === seen.size && new Set(values).size === values.length,
        values.join(' | '));

    // A derived channel must be labelled where a reader will see it. The general console
    // discovers whatever arrives and drew every channel identically, so an efficiency computed
    // from four measurements looked exactly like a fifth measurement.
    const cardMarkup = elements.get('grid').children.map(c => String(c.innerHTML));
    const derivedCards = cardMarkup.filter(m => m.includes('class="derived"'));
    const derivedStreamed = [...seen].filter(v => derivedByVariable.has(v));

    if (derivedStreamed.length === 0) {
        check('a derived channel reached the stream, without which this check tests nothing',
            false,
            'rerun the host with --computed, or with a profile that declares computed channels');
    } else {
        check('every derived channel is labelled as computed, and no measurement is',
            derivedCards.length === derivedStreamed.length
            && derivedStreamed.every(v => cardMarkup.some(m => m.includes('class="derived"') && m.includes(v))),
            `${derivedCards.length} labelled for ${derivedStreamed.length} derived: ${derivedStreamed.join(', ')}`);
    }

    // A limit breach must be visible, and visibly not the same thing as an anomaly. The z-score
    // cannot raise this alarm at all: a channel sitting steadily outside a hard limit is not
    // unusual for itself, which is exactly why the limit exists.
    const breachedVars = [...breachedByVariable];
    if (breachedVars.length === 0) {
        check('a limit breach reached the stream, without which this check tests nothing',
            false,
            'rerun the host with a limit the data breaches, e.g. --limit "grid.voltage[V] < 300"');
    } else {
        // Read from the verdict element, not from the card's innerHTML. The label is assigned as
        // textContent on a child the card created, so the card's markup string never contains it —
        // asserting on that string was an assertion about this shim, and it failed a page that was
        // doing the right thing.
        const verdictOf = card => {
            const el = [...elements.entries()].find(([k]) => k === 'a-' + card._id);
            return el ? String(el[1].textContent) : '';
        };

        const labelled = elements.get('grid').children
            .filter(c => verdictOf(c) === '한계 초과')
            .map(c => String(c.innerHTML));

        check('a channel outside its limit is labelled as such rather than as an anomaly',
            labelled.length >= 1 && breachedVars.every(v => labelled.some(m => m.includes(v))),
            `${labelled.length} card(s) labelled for ${breachedVars.length} breached: ${breachedVars.join(', ')}`);
    }

    // The endpoints the console calls, exercised for real.
    sandbox.document.getElementById('spec-ch').value = [...elements.get('spec-ch').children]
        .map(o => o.value).find(Boolean) || '';

    elements.get('spec-go').fire('click');
    elements.get('dvr-load').fire('click');

    setTimeout(() => {
        check('the spectrum endpoint answered for a discovered channel',
            /샘플|스펙트럼 없음/.test(elements.get('spec-stat').textContent),
            elements.get('spec-stat').textContent);
        check('the spectrum request named a channel the console discovered',
            fetches.some(u => u.startsWith('/api/spectrum?channel=')),
            fetches.filter(u => u.includes('spectrum')).join(' '));
        check('the DVR endpoint answered',
            /초 분량|프레임/.test(
                elements.get('dvr-range').textContent + elements.get('dvr-stat').textContent),
            elements.get('dvr-range').textContent + ' / ' + elements.get('dvr-stat').textContent);

        console.log(`\n${failures === 0 ? 'all checks passed' : failures + ' check(s) failed'}`);
        process.exit(failures === 0 ? 0 : 1);
    }, 4000);
}
