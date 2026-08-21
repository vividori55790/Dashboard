// Checks every bundled example page against a running host.
//
// Usage:
//   TelemetryDashboard.Host.exe --simulate --port 8099
//   node verify_starters.js 8099
//
// These pages are teaching material: someone copies one and builds on it. Every one of them used
// to read data.temp, data.humidity, data.rpm and data.vibration -- fields the hub does not send --
// so a reader who copied one got a page that showed nothing, and had no way to tell whether the
// fault was theirs.
//
// Two of them did worse than show nothing. starter_minimal matched channel names by substring and
// ended with an `else` that wrote every unrecognised channel into the temperature box.
//
// So what is checked is narrow: does each page put a real reading on screen, and does it keep
// separate channels separate.
const fs = require('fs');
const http = require('http');
const vm = require('vm');

const port = Number(process.argv[2] || 8099);

const PAGES = [
    { file: 'starter_minimal.html',        expect: 'many' },
    { file: 'starter_grid_dashboard.html', expect: 'many' },
    { file: 'starter_chart_gauge.html',    expect: 'one'  },
    { file: 'custom_widget.html',          expect: 'one'  }
];

let failures = 0;
function check(name, ok, detail) {
    console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -- ' + detail : ''}`);
    if (!ok) failures++;
}

function makeDom() {
    const elements = new Map();

    function makeEl(id, tag) {
        const el = {
            _id: id, tag: tag || 'div', textContent: '', _innerHTML: '',
            style: {}, children: [], cells: [], clientWidth: 400,
            appendChild(c) { this.children.push(c); if (c.tag === 'tr') this.cells = c.cells; return c; },
            querySelector(sel) {
                const cls = String(sel).replace('.', '');
                return this._byClass?.[cls] || null;
            },
            getContext: () => ({
                clearRect() {}, beginPath() {}, moveTo() {}, lineTo() {}, stroke() {},
                set strokeStyle(v) {}, set lineWidth(v) {}
            }),
            classList: null,
            get innerHTML() { return this._innerHTML; },
            set innerHTML(v) {
                this._innerHTML = String(v);
                this._byClass = {};
                // Register children by class and by id, enough for these pages to find them again.
                for (const m of this._innerHTML.matchAll(/class="([^"]+)"/g)) {
                    this._byClass[m[1].split(' ')[0]] = makeEl('', 'div');
                }
                this.cells = [...this._innerHTML.matchAll(/<td[^>]*>([^<]*)/g)]
                    .map(() => makeEl('', 'td'));
                for (const m of this._innerHTML.matchAll(/id="([^"]+)"/g)) {
                    if (!elements.has(m[1])) elements.set(m[1], makeEl(m[1], 'div'));
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

    const document = {
        getElementById: id => elements.get(id) || (elements.set(id, makeEl(id)), elements.get(id)),
        createElement: tag => makeEl('', tag),
        querySelector: sel => elements.get(sel) || makeEl(sel),
        addEventListener: () => {}
    };

    return { elements, document, makeEl };
}

function runPage(file) {
    const html = fs.readFileSync(file, 'utf8');
    const { elements, document } = makeDom();

    let dataCb = null, statusCb = null, connectedTo = null;
    const TelemetryClient = {
        connect(u) { connectedTo = u; return this; },
        onData(cb) { dataCb = cb; return this; },
        onStatusChange(cb) { statusCb = cb; cb('DISCONNECTED', false); return this; }
    };

    const sandbox = {
        document, console, TelemetryClient,
        setInterval: () => 0,
        location: { hostname: 'localhost', port: String(port), search: '' },
        URLSearchParams: class { constructor() {} get() { return null; } has() { return false; } },
        Number, Math, Date, JSON, Object, Array, String
    };

    const scripts = [...html.matchAll(/<script>([\s\S]*?)<\/script>/g)].map(m => m[1]);
    vm.createContext(sandbox);
    vm.runInContext(scripts[scripts.length - 1], sandbox);

    return { elements, dataCb, statusCb, connectedTo, html };
}

// ---- collect real packets once, replay into every page ---------------------
const captured = [];
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
            if (typeof p.variable === 'string') captured.push(p);
        }
        if (captured.length >= 40) { req.destroy(); replay(); }
    });
});
req.on('error', e => { console.error('FAIL: cannot read stream:', e.message); process.exit(1); });
setTimeout(() => { req.destroy(); replay(); }, 20000);

let done = false;
function replay() {
    if (done) return;
    done = true;

    const distinct = new Set(captured.map(p => p.variable));
    console.log(`captured ${captured.length} packet(s) across ${distinct.size} channel(s)\n`);

    for (const page of PAGES) {
        let ctx;
        try { ctx = runPage(page.file); }
        catch (e) { check(`${page.file} loads`, false, e.message); continue; }

        check(`${page.file}: reads none of the fields the hub never sends`,
            !/data\.temp\b|data\.vin\b|data\.humidity\b|data\.rpm\b|data\.vibration\b/.test(
                ctx.html.replace(/<!--[\s\S]*?-->/g, '')),
            'data.temp / data.vin / data.humidity / data.rpm / data.vibration');

        check(`${page.file}: connects to the port it was served from`,
            ctx.connectedTo === `ws://localhost:${port}/ws`, ctx.connectedTo);

        captured.forEach(p => { try { ctx.dataCb(p); } catch (e) { /* reported below */ } });

        // Something on the page has to carry a number that arrived. The walk includes children
        // and table cells: a page that writes into row.cells[2] is not less correct than one that
        // writes into a element it looked up by id, and a scan that missed those would be an
        // assertion about the harness.
        const texts = [];
        const walk = (e, depth) => {
            if (!e || depth > 6) return;
            texts.push(String(e.innerHTML || ''), String(e.textContent || ''));
            (e.children || []).forEach(c => walk(c, depth + 1));
            (e.cells || []).forEach(c => walk(c, depth + 1));
        };
        [...ctx.elements.values()].forEach(e => walk(e, 0));

        const rendered = texts.filter(t => /\d/.test(t));

        check(`${page.file}: puts a real reading on screen`,
            rendered.length > 0, `${rendered.length} element(s) hold a number`);
    }

    console.log(`\n${failures === 0 ? 'all checks passed' : failures + ' check(s) failed'}`);
    process.exit(failures === 0 ? 0 : 1);
}
