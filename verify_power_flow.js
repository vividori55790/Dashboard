// Runs power_flow.js against a DOM stub and checks what it draws for data it does not have.
//
// Usage:
//   node verify_power_flow.js            (offline: the three states below)
//   node verify_power_flow.js 8123       (also pulls one real /api/computed reply from a host)
//
// The claim being checked is narrow and is the whole reason this diagram is difficult: a quantity
// nobody reported must not be drawn as zero. A still link and a zero-watt link look identical, so
// "unknown" has to be visibly a different thing from "nothing is flowing" — otherwise the picture
// invents a fact for every channel that has gone quiet.
//
// What this cannot tell you: whether the diagram looks right. Layout, colour and animation need a
// browser and an eye.
const fs = require('fs');
const http = require('http');
const vm = require('vm');

const source = fs.readFileSync('power_flow.js', 'utf8');
const port = Number(process.argv[2] || 0);

// ---- minimal DOM ----------------------------------------------------------
// Attributes and text are recorded rather than rendered; every assertion below is about what the
// module asked the DOM for, which is the only thing a stub can honestly report.
function makeNode(name) {
    const node = {
        nodeName: name,
        attrs: {},
        children: [],
        _text: '',
        style: {},
        setAttribute(k, v) { this.attrs[k] = String(v); },
        getAttribute(k) { return this.attrs[k]; },
        removeAttribute(k) { delete this.attrs[k]; },
        appendChild(c) { this.children.push(c); return c; },
        removeChild(c) { this.children = this.children.filter(x => x !== c); return c; },
        get textContent() { return this._text; },
        set textContent(v) { this._text = String(v); },
        get innerHTML() { return this._html || ''; },
        set innerHTML(v) { this._html = String(v); this.children = []; },
        classList: {
            _s: new Set(),
            add(...n) { n.forEach(x => this._s.add(x)); },
            remove(...n) { n.forEach(x => this._s.delete(x)); },
            toggle(n, on) { on ? this._s.add(n) : this._s.delete(n); },
            contains(n) { return this._s.has(n); }
        },
        querySelector: () => null,
        querySelectorAll: () => []
    };
    node.classList = Object.create(node.classList);
    node.classList._s = new Set();
    return node;
}

const document = {
    createElementNS: (_ns, name) => makeNode(name),
    createElement: name => makeNode(name)
};

const sandbox = {
    document,
    window: {},
    console,
    // Animation is driven by rAF. Held rather than run: a frame loop in a test process either spins
    // forever or measures the harness's own scheduler, and neither says anything about the diagram.
    requestAnimationFrame: () => 0,
    cancelAnimationFrame: () => {},
    Date, Math, Number, String, Array, Object, JSON, Set, Map
};
sandbox.globalThis = sandbox;
sandbox.self = sandbox;

vm.createContext(sandbox);
vm.runInContext(source, sandbox);

const PowerFlow = sandbox.window.PowerFlow || sandbox.PowerFlow;

let failures = 0;
function check(name, ok, detail) {
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -- ' + detail : ''}`);
    if (!ok) failures++;
}

// Every string the module put on screen, wherever it put it.
function textOf(node, out) {
    out = out || [];
    // The stylesheet the module injects is a text node too, and it is far longer than every label
    // put together -- collecting it buried the labels this file exists to read.
    if (String(node.nodeName).toLowerCase() === 'style') return out;
    if (node._text) out.push(node._text);
    (node.children || []).forEach(c => textOf(c, out));
    return out;
}

function drawnText(container) {
    return textOf(container).join(' | ');
}

check('the module exposes mount and update',
    PowerFlow && typeof PowerFlow.mount === 'function' && typeof PowerFlow.update === 'function',
    PowerFlow ? Object.keys(PowerFlow).join(', ') : 'no PowerFlow global');

if (!PowerFlow) { process.exit(1); }

// ---- 1. mounted with nothing ----------------------------------------------
const empty = makeNode('div');
let threw = null;
try { PowerFlow.mount(empty); PowerFlow.update({}); } catch (e) { threw = e; }

check('mounting before any data does not throw', threw === null, threw ? String(threw) : '');
check('a diagram with no data drawn at all still renders its stages',
    empty.children.length > 0, `${empty.children.length} child element(s)`);

// Labels only. The module ships a legend that explains in prose how 값 없음 differs from 0 W, and
// the first version of this check matched that explanation as though it were a reading — a harness
// failing a module for documenting the very property the harness was written to enforce.
const labelsOnly = c => textOf(c).filter(t => t.length <= 40).join(' | ');

const blank = labelsOnly(empty);
check('with nothing known, the picture says so rather than showing zeros',
    blank.includes('값 없음') && !/\d\s*(W|kW)\b/.test(blank),
    blank.slice(0, 200));

// ---- 2. a full, healthy chain ---------------------------------------------
const now = Date.now();
const full = {
    'grid.voltage':        { value: 380, at: now, unit: 'V' },
    'dab.bus_voltage':     { value: 400, at: now, unit: 'V' },
    'dab.input_current':   { value: 25,  at: now, unit: 'A' },
    'psfb.output_voltage': { value: 48,  at: now, unit: 'V' },
    'psfb.output_current': { value: 190, at: now, unit: 'A' },
    'server.load':         { value: 82,  at: now, unit: '%' },
    'dab.p_in':            { value: 10000, at: now, unit: 'W', derived: true },
    'psfb.p_out':          { value: 9120,  at: now, unit: 'W', derived: true },
    'psfb.efficiency':     { value: 91.2,  at: now, unit: '%', derived: true }
};

const healthy = makeNode('div');
threw = null;
try { PowerFlow.mount(healthy); PowerFlow.update(full); } catch (e) { threw = e; }
check('a full chain renders without throwing', threw === null, threw ? String(threw) : '');

const healthyText = drawnText(healthy);
check('the powers it was given appear on the picture',
    /10(\.\d+)?\s*kW|10000/.test(healthyText) && /9\.1\d*\s*kW|9120/.test(healthyText),
    healthyText.slice(0, 200));

check('efficiency is shown and marked as computed rather than measured',
    healthyText.includes('91.2') && healthyText.includes('계산값'),
    healthyText.slice(0, 200));

// ---- 3. one input missing, one stale --------------------------------------
// The case the whole design is for: p_in cannot be computed here, and the module must not multiply
// V by I itself. Those two arrive at different instants and only the host aligns them.
const partial = Object.assign({}, full);
delete partial['dab.p_in'];
delete partial['psfb.efficiency'];
partial['psfb.output_current'] = { value: 190, at: now - 60000, unit: 'A' };

const degraded = makeNode('div');
threw = null;
try { PowerFlow.mount(degraded); PowerFlow.update(partial); } catch (e) { threw = e; }
check('a chain with a gap in it renders without throwing', threw === null, threw ? String(threw) : '');

const degradedText = drawnText(degraded);
check('a power nobody reported is 값 없음, not 0 W',
    degradedText.includes('값 없음'),
    degradedText.slice(0, 220));

check('the module does not compute the missing power from V and I itself',
    !degradedText.includes('10000') && !/10\.\d+\s*kW/.test(degradedText),
    'V=400 and I=25 are both present; their product must not appear');

check('a sample a minute old is shown as old rather than as current',
    /초|분|오래|stale/i.test(degradedText),
    degradedText.slice(0, 220));

// ---- 4. a breach must be unmistakable -------------------------------------
const breached = makeNode('div');
const breachState = Object.assign({}, full, {
    'dab.bus_voltage': { value: 460, at: now, unit: 'V', limitBreach: true }
});
threw = null;
try { PowerFlow.mount(breached); PowerFlow.update(breachState); } catch (e) { threw = e; }
check('a limit breach renders without throwing', threw === null, threw ? String(threw) : '');

function classesOf(node, out) {
    out = out || [];
    if (node.classList && node.classList._s.size) out.push(...node.classList._s);
    if (node.attrs && node.attrs.class) out.push(node.attrs.class);
    (node.children || []).forEach(c => classesOf(c, out));
    return out;
}
const breachClasses = classesOf(breached).join(' ');
check('a channel outside its limit is marked on the diagram',
    /breach|alarm/i.test(breachClasses),
    breachClasses.slice(0, 160) || '(no classes set)');

// ---- 5. efficiency above 100 is shown, not hidden -------------------------
// Simulated channels wander independently on purpose, so this happens. Clamping it would hide a
// real property of the data, and hiding it is how a demo stops telling the truth.
const over = makeNode('div');
threw = null;
try {
    PowerFlow.mount(over);
    PowerFlow.update(Object.assign({}, full, {
        'psfb.efficiency': { value: 116.1, at: now, unit: '%', derived: true }
    }));
} catch (e) { threw = e; }
check('an efficiency above 100% is displayed rather than clamped or dropped',
    threw === null && drawnText(over).includes('116'),
    threw ? String(threw) : drawnText(over).slice(0, 200));

// ---- 6. against a live host ------------------------------------------------
if (!port) { finish(); }
else {
    http.get({ host: 'localhost', port, path: '/api/computed' }, res => {
        let body = '';
        res.on('data', c => { body += c; });
        res.on('end', () => {
            let payload;
            try { payload = JSON.parse(body); } catch { return finish(); }

            const live = {};
            (payload.Channels || []).forEach(c => {
                if (c.Value !== null && c.Value !== undefined) {
                    live[c.Id] = { value: c.Value, at: Date.now(), unit: c.Unit, derived: true };
                }
            });

            const fromHost = makeNode('div');
            let err = null;
            try { PowerFlow.mount(fromHost); PowerFlow.update(live); } catch (e) { err = e; }

            check('the shapes a real host serves render without throwing',
                err === null,
                err ? String(err) : `${Object.keys(live).length} channel(s) from /api/computed`);
            finish();
        });
    }).on('error', e => {
        check('a host was reachable for the live check', false, e.message);
        finish();
    });
}

function finish() {
    console.log(`\n${failures === 0 ? 'all checks passed' : failures + ' check(s) failed'}`);
    process.exit(failures === 0 ? 0 : 1);
}
