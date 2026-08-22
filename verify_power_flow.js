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

// Everything the module positions has to land inside the box it declared. SVG does not report a
// label placed past the bottom edge — it draws nothing and says nothing — so a legend that grew by
// one line silently lost its last sentence, which is how this check came to exist.
const svgRoot = empty.children[0];
const vb = (svgRoot.attrs.viewBox || '').split(/\s+/).map(Number);
function placed(node, out) {
    out = out || [];
    if (node.attrs && node.attrs.y !== undefined && node.attrs.x !== undefined) {
        out.push({ x: Number(node.attrs.x), y: Number(node.attrs.y),
                   h: Number(node.attrs.height || 0), t: (node._text || '').slice(0, 24) });
    }
    (node.children || []).forEach(c => placed(c, out));
    return out;
}
const outside = placed(svgRoot).filter(e => e.y > vb[3] || e.y + e.h > vb[3] || e.x > vb[2] || e.y < 0);
check('nothing is positioned outside the box the diagram declares',
    vb.length === 4 && outside.length === 0,
    outside.length ? `viewBox height ${vb[3]}, outside: ` +
        outside.map(e => `y=${e.y + e.h} "${e.t}"`).join(', ') : `viewBox ${svgRoot.attrs.viewBox}`);

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

// ---- 6. the UPS branch, and the T it hangs from ----------------------------
// The picture is a T on purpose: the top bar is the normal feed and the stem drops from the DC bus
// to a battery branch wired in parallel with it. That shape is the claim being made about the
// hardware, so it is asserted here rather than eyeballed — a diagram that quietly relaxes into one
// straight line is still a working diagram, and it would be describing a different machine.
const ups = makeNode('div');
const upsState = Object.assign({}, full, {
    'ups.battery_voltage': { value: 51.2,  at: now, unit: 'V' },
    'ups.battery_current': { value: -180,  at: now, unit: 'A' },
    'ups.bus_current':     { value: 23,    at: now, unit: 'A' },
    'ups.state_of_charge': { value: 87.4,  at: now, unit: '%' },
    'ups.p_batt':          { value: -9216, at: now, unit: 'W', derived: true },
    'ups.p_bus':           { value: 9200,  at: now, unit: 'W', derived: true }
});
threw = null;
try { PowerFlow.mount(ups); PowerFlow.update(upsState); } catch (e) { threw = e; }
check('the UPS branch renders without throwing', threw === null, threw ? String(threw) : '');

const upsText = drawnText(ups);
check('the battery branch draws its own channels rather than borrowing the chain\'s',
    upsText.includes('ups.battery_voltage')
    && upsText.includes('ups.battery_current')
    && upsText.includes('ups.state_of_charge'),
    upsText.slice(0, 200));

// Geometry, which nothing here used to look at. Every box carries x/y, so "below" and "vertical"
// are checkable facts rather than a thing the author remembers doing.
function boxes(node, out) {
    out = out || [];
    if (String(node.nodeName).toLowerCase() === 'rect'
        && node.attrs && node.attrs.class === 'pf-box') {
        out.push({ x: Number(node.attrs.x), y: Number(node.attrs.y) });
    }
    (node.children || []).forEach(c => boxes(c, out));
    return out;
}
const rects = boxes(ups);
const ys = [...new Set(rects.map(b => b.y))].sort((a, b) => a - b);
check('the diagram is two rows, not one line',
    rects.length >= 6 && ys.length === 2 && ys[1] > ys[0],
    `${rects.length} boxes on row y=${ys.join(' and y=')}`);

// The stem. A link drawn between two rows has to actually be vertical; drawn as a horizontal line
// between two boxes at different heights it would still render, and would look like a diagonal or
// like nothing at all.
function railLines(node, out) {
    out = out || [];
    const cls = node.attrs && node.attrs.class;
    if (String(node.nodeName).toLowerCase() === 'line' && cls && cls.indexOf('pf-rail') === 0) {
        out.push(node.attrs);
    }
    (node.children || []).forEach(c => railLines(c, out));
    return out;
}
const rails = railLines(ups);
const stems = rails.filter(r => Number(r.x1) === Number(r.x2) && Number(r.y1) !== Number(r.y2));
// Only that it is vertical and lands between the two rows. Which end is y1 depends on which way
// the link is declared to run, and the stem runs upward on purpose -- an assertion that pinned the
// coordinate order would be pinning the sign convention by accident.
const spans = stems.filter(r => Math.min(+r.y1, +r.y2) > ys[0] && Math.max(+r.y1, +r.y2) < ys[1]);
check('a vertical stem joins the two rows',
    stems.length === 1 && spans.length === 1,
    `${rails.length} links, ${stems.length} vertical, ${spans.length} between the rows`);

// Read one link's own labels, not the whole picture. Written the lazy way first, and all three of
// the checks below failed on the legend and on a tooltip: the legend explains what 충전 and 방전
// mean and names ups.p_batt while doing it, and the battery-current row's tooltip says "양수 =
// 충전". Both are the module documenting itself, and a harness that reads them is grading the
// explanation instead of the drawing — the same mistake this file already made once, recorded at
// the labelsOnly comment above.
function linkTextFor(container, id) {
    const found = [];
    (function walk(node) {
        const cls = node.attrs && node.attrs.class;
        if (String(node.nodeName).toLowerCase() === 'g' && cls && cls.indexOf('pf-link') === 0) {
            const t = textOf(node).join(' | ');
            if (t.includes(id)) found.push(t);
        }
        (node.children || []).forEach(walk);
    })(container);
    return found.join(' || ');
}

// The rule the whole file is built on, applied to the new branch. The two sides of the UPS
// converter differ by its loss, so each segment names its own channel; drawing one figure on both
// would assert a loss of zero that nobody measured.
check('each side of the UPS converter names its own power channel, not one figure drawn twice',
    linkTextFor(ups, 'ups.p_batt').split('ups.p_batt').length - 1 === 1
    && linkTextFor(ups, 'ups.p_bus').split('ups.p_bus').length - 1 === 1
    && !linkTextFor(ups, 'ups.p_bus').includes('ups.p_batt'),
    `stem: ${linkTextFor(ups, 'ups.p_bus').slice(0, 80)} || battery: ${linkTextFor(ups, 'ups.p_batt').slice(0, 80)}`);

// The stem's direction, which is the whole point of drawing this as a T. During an outage the
// battery branch is what holds the chain up, and the picture has to show power entering the top
// bar from below -- an arrow pointing the other way would be describing a UPS being charged by a
// grid that is not there.
function arrowTipOf(container, id) {
    let tip = null;
    (function walk(node) {
        const cls = node.attrs && node.attrs.class;
        if (String(node.nodeName).toLowerCase() === 'g' && cls && cls.indexOf('pf-link') === 0
            && textOf(node).join(' ').includes(id)) {
            (function find(n) {
                if (String(n.nodeName).toLowerCase() === 'path' && n.attrs && n.attrs.d) {
                    // "M bx,by L tipX,tipY L bx2,by2 Z" -- the middle vertex is the point.
                    const pts = n.attrs.d.match(/-?[\d.]+,-?[\d.]+/g) || [];
                    if (pts.length === 3) {
                        const p = pts.map(q => q.split(',').map(Number));
                        tip = { tip: p[1], back: [(p[0][1] + p[2][1]) / 2] };
                    }
                }
                (n.children || []).forEach(find);
            })(node);
        }
        (node.children || []).forEach(walk);
    })(container);
    return tip;
}
const stemUp = arrowTipOf(ups, 'ups.p_bus');
check('the UPS branch feeds the chain from below: its arrow points up while it supports the bus',
    stemUp !== null && stemUp.tip[1] < stemUp.back[0],
    stemUp ? `tip y=${stemUp.tip[1]}, tail y=${stemUp.back[0]}` : 'no arrow found on the stem');

const chargingStem = makeNode('div');
PowerFlow.mount(chargingStem);
PowerFlow.update(Object.assign({}, upsState, {
    'ups.bus_current': { value: -3.1,  at: now, unit: 'A' },
    'ups.p_bus':       { value: -1240, at: now, unit: 'W', derived: true }
}));
const stemDown = arrowTipOf(chargingStem, 'ups.p_bus');
check('and points down again when the bus is charging the bank instead',
    stemDown !== null && stemDown.tip[1] > stemDown.back[0],
    stemDown ? `tip y=${stemDown.tip[1]}, tail y=${stemDown.back[0]}` : 'no arrow found on the stem');

const upsLink = linkTextFor(ups, 'ups.p_batt');
check('a discharging bank is drawn as discharging, with the sign it was given',
    upsLink.includes('방전') && !upsLink.includes('충전') && upsLink.includes('-9.22 kW'),
    upsLink.slice(0, 160));

// Direction is the one thing a sign convention exists to get right, so both ways are checked.
// A diagram that says 방전 whatever the sign would pass a test that only ever discharges.
const charging = makeNode('div');
PowerFlow.mount(charging);
PowerFlow.update(Object.assign({}, upsState, {
    'ups.battery_current': { value: 40,   at: now, unit: 'A' },
    'ups.p_batt':          { value: 2048, at: now, unit: 'W', derived: true }
}));
const chargeLink = linkTextFor(charging, 'ups.p_batt');
check('a charging bank is drawn as charging',
    chargeLink.includes('충전') && !chargeLink.includes('방전') && chargeLink.includes('2.05 kW'),
    chargeLink.slice(0, 160));

// Exactly zero is neither, and saying so matters here more than elsewhere: a bank sitting at 0 W
// is doing nothing, and labelling that 충전 makes an idle UPS look like it is filling up.
const idle = makeNode('div');
PowerFlow.mount(idle);
PowerFlow.update(Object.assign({}, upsState, {
    'ups.p_batt': { value: 0, at: now, unit: 'W', derived: true }
}));
const idleLink = linkTextFor(idle, 'ups.p_batt');
check('a bank moving no power is called neither charging nor discharging',
    !idleLink.includes('충전') && !idleLink.includes('방전') && idleLink.includes('0.0 W'),
    idleLink.slice(0, 160));

// ---- 7. against a live host ------------------------------------------------
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
