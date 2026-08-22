/*
    power_flow.js — DAB/PSFB 전력 변환 체인의 실시간 흐름도.

    dab_psfb_console.html 옆에 붙는 그림이고, 규칙은 그 페이지와 같습니다: 화면에 있는 모든
    숫자는 방금 도착한 값이거나, 아니면 비어 있어야 합니다.

    다만 흐름도에는 카드에 없는 함정이 하나 더 있습니다. 선은 값이 없어도 그려집니다. 0 W 를
    정지한 선으로 그리고 값을 모르는 구간도 정지한 선으로 그리면, 화면에서 그 둘은 완전히
    같아집니다 — "이 구간에 부하가 없다" 와 "이 구간의 전력을 모른다" 는 운영자에게 정반대의
    뜻인데도 그렇습니다. 그래서 여기서는

        모름  = 회색 점선 + '값 없음' + 정지 (속이 빈 화살표)
        0 W   = 살아 있는 실선 + '0 W' + 정지 (채워진 화살표)

    두 상태가 절대 같아 보이지 않게 그립니다.

    없는 값을 여기서 만들어내지도 않습니다. p_in 이 없다고 V x I 를 이 파일에서 곱하면 안
    됩니다. 전압과 전류는 서로 다른 순간에 도착하고, 그 둘을 한 시점으로 맞추는 일은 호스트가
    /api/aligned 위에서 합니다. 지금 전압에 300 ms 전 전류를 곱한 전력은 실제로 흐른 적이 없는
    전력이고, 다른 숫자들과 똑같은 모습으로 출력됩니다. 모르면 '값 없음' 이라고 씁니다.

    호출 규약 (전역 하나만 노출합니다):
        PowerFlow.mount(containerElement)   SVG 를 한 번 만든다
        PowerFlow.update(state)             초당 4회 정도, 가장 최근에 알려진 값으로 갱신한다

    state 는 채널 id -> { value, at, derived, limitBreach, unit } 이고, 한 번도 보고된 적 없는
    채널은 키 자체가 없습니다. 그 구분이 이 파일이 하는 일의 거의 전부입니다.
*/
(function (root) {
    'use strict';

    var NS = 'http://www.w3.org/2000/svg';

    // 좌표. viewBox 로만 그리고 픽셀 폭을 고정하지 않으므로 컨테이너 폭에 맞춰 늘어납니다.
    var W = 1160, H = 286;
    var MARGIN = 16, BOX_W = 168, BOX_H = 150, BOX_Y = 30, GAP = 152;
    var MID = BOX_Y + BOX_H / 2;   // 105 — 링크가 지나는 높이이자 상자의 세로 중앙
    var PAD = 14;                  // 상자 안쪽 여백
    var ROW_H = 44;                // 값 한 줄(숫자 + 채널 id)이 차지하는 높이

    var STALE_MS = 10000;          // dab_psfb_console.html 과 같은 기준
    var DASH = 24;                 // 파선 한 주기 (14 + 10). 오프셋을 이 값으로 접습니다.

    // 굵기와 속도를 정하려면 전력의 눈금이 필요합니다. 이 값은 측정값이 아니라 그리기 눈금이며,
    // 프로파일이 선언한 범위에서 옵니다: DAB 450 V x 40 A = 18 kW, PSFB 54 V x 260 A = 14 kW.
    // 화면에 들어온 최대값으로 자동 조정하지 않는 이유는, 그러면 한 링크의 굵기가 다른 링크의
    // 값에 따라 변하기 때문입니다 — 같은 전력이 시각마다 다른 굵기로 보이는 쪽이 더 나쁩니다.
    var FULL_SCALE_W = 18000;

    // 단계. id 는 프레임에 실제로 실려 오는 문자열이므로 정확히 일치시킵니다. 부분 문자열로
    // 맞추면 grid.voltage 와 psfb.output_voltage 가 서로의 칸을 덮어씁니다 — 이 프로젝트가
    // 이미 한 번 겪은 결함입니다.
    var STAGES = [
        { name: '계통', tag: 'grid', rows: [
            { id: 'grid.voltage', unit: 'V', dp: 0, label: '계통 전압' }
        ] },
        { name: 'DAB 배터리 컨버터', tag: 'dab', rows: [
            { id: 'dab.bus_voltage', unit: 'V', dp: 0, label: 'DAB 출력 버스 전압' },
            { id: 'dab.input_current', unit: 'A', dp: 2, label: 'DAB 입력 전류' }
        ] },
        { name: 'PSFB 48 V 레일', tag: 'psfb', rows: [
            { id: 'psfb.output_voltage', unit: 'V', dp: 2, label: 'PSFB 출력 전압' },
            { id: 'psfb.output_current', unit: 'A', dp: 1, label: 'PSFB 출력 전류' }
        ] },
        { name: '서버 부하', tag: 'server', rows: [
            { id: 'server.load', unit: '%', dp: 1, label: '서버 부하율' }
        ] }
    ];

    // 링크. 가운데 구간에 전력 채널이 없는 것은 버그가 아니라 이 장비의 사실입니다. 호스트가
    // 계산하는 전력은 dab.p_in 과 psfb.p_out 둘뿐이고, DAB 와 PSFB 사이를 재는 채널은 선언된
    // 적이 없습니다. 그래서 p_in 을 두 링크에 겹쳐 그리지 않습니다 — 같은 전력이 두 구간을
    // 그대로 흐른다고 말하는 것이 되고, 그것은 손실이 0 이라는, 아무도 측정하지 않은 주장입니다.
    //
    // p_in 을 첫 링크(계통 -> DAB)에 두는 근거는 채널 이름 자체입니다: DAB 로 들어가는 전력.
    // 다만 그 식은 dab.bus_voltage * dab.input_current 로, 출력측 전압과 입력측 전류를 곱한
    // 것입니다. 그래서 값 밑에 항상 채널 id 를 같이 적습니다 — 이 그림이 붙인 이름이 아니라
    // 호스트가 선언한 채널이라는 것을 읽는 사람이 확인할 수 있어야 합니다.
    var LINKS = [
        { power: 'dab.p_in', unit: 'W', noChannel: null },
        { power: null, noChannel: '구간 전력 채널 없음', badges: true },
        { power: 'psfb.p_out', unit: 'W', noChannel: null }
    ];

    // 색은 전부 페이지의 CSS 변수로 갑니다. 이 그림만 다른 팔레트로 도는 일이 없어야 합니다.
    // --accent 와 --warn 만 대체값을 적어 두는데, 이 둘은 이 컴포넌트가 요구하는 변수 목록에
    // 없어서 없는 페이지에 얹힐 수 있기 때문입니다. 나머지는 없으면 그대로 드러나는 편이 낫습니다.
    var CSS = [
        '.pf-box{fill:var(--panel);stroke:var(--line);stroke-width:1}',
        '.pf-box.pf-alarm{stroke:var(--alarm);stroke-width:2}',
        '.pf-tint{fill:var(--alarm);opacity:0}',
        '.pf-tint.pf-on{opacity:0.09}',
        '.pf-div{stroke:var(--line);stroke-width:1}',
        '.pf-name{fill:var(--text-2);font-size:13px;font-weight:600}',
        '.pf-name.pf-alarm{fill:var(--alarm)}',
        '.pf-badge{fill:var(--alarm);font-size:10px;font-weight:700}',
        '.pf-sub{fill:var(--text-3);font-size:10px}',
        '.pf-num{fill:var(--text);font-size:20px;font-weight:600}',
        '.pf-num.pf-absent{fill:var(--text-3);font-weight:400}',
        '.pf-num.pf-alarm{fill:var(--alarm)}',
        '.pf-unit{fill:var(--text-3);font-size:11.5px;font-weight:500}',
        '.pf-id{fill:var(--text-3);font-size:9px}',
        '.pf-status{fill:var(--text-3);font-size:9.5px}',
        '.pf-row.pf-stale{opacity:0.45}',
        '.pf-rail{fill:none;stroke:var(--line);stroke-width:12;stroke-linecap:round}',
        '.pf-rail.pf-unknown{stroke-width:2;stroke-dasharray:6 7}',
        '.pf-flow{fill:none;stroke:var(--accent,#3D8BFF);stroke-linecap:butt;stroke-dasharray:14 10}',
        '.pf-flow.pf-alarm{stroke:var(--alarm)}',
        '.pf-arrow{fill:var(--accent,#3D8BFF);stroke:none}',
        '.pf-arrow.pf-alarm{fill:var(--alarm)}',
        '.pf-arrow.pf-unknown{fill:none;stroke:var(--line);stroke-width:1.5}',
        '.pf-power{fill:var(--text);font-size:15px;font-weight:600}',
        '.pf-power.pf-absent{fill:var(--text-3);font-size:13px;font-weight:400}',
        '.pf-power.pf-alarm{fill:var(--alarm)}',
        '.pf-cap{fill:var(--text-3);font-size:8.5px}',
        '.pf-eff{fill:var(--text);font-size:13.5px;font-weight:600}',
        '.pf-eff.pf-absent{fill:var(--text-3);font-size:12px;font-weight:400}',
        '.pf-eff.pf-alarm{fill:var(--alarm)}',
        '.pf-ratio{fill:var(--text-2);font-size:11px}',
        '.pf-ratio.pf-absent{fill:var(--text-3)}',
        '.pf-mark{fill:var(--text-2);font-size:8.5px}',
        '.pf-over{fill:var(--warn,#FFB020);font-size:9px}',
        '.pf-pill-bg{fill:var(--panel-2);stroke:var(--line);stroke-width:1}',
        '.pf-pill-t{fill:var(--text-2);font-size:9px;font-weight:600}',
        '.pf-legend{fill:var(--text-3);font-size:11px}',
        '.pf-mono{font-family:"JetBrains Mono",ui-monospace,Consolas,monospace}',
        '.pf-stale{opacity:0.45}',
        '.pf-hide{display:none}'
    ].join('');

    var LEGEND = [
        '값 없음 = 그 값이 한 번도 도착하지 않았다는 뜻입니다. 회색 점선에 속이 빈 화살표로 그립니다 — 0 W 가 아닙니다.',
        '0 W 는 값이 있는 상태이므로 실선으로 그리되 움직이지 않습니다. 흐르는 파선의 속도와 굵기는 전력에 비례합니다 (전 구간 같은 눈금, 18 kW = 최대).',
        '계산값 = 호스트가 여러 채널을 한 시점으로 맞춰 계산한 값입니다. 어떤 장비도 이 값을 보고하지 않으며, 이 그림은 없는 값을 대신 계산하지 않습니다.',
        '10초 넘게 소식이 없는 값은 흐리게 표시하고 경과 시간을 씁니다 — 멈춘 값이 살아 있는 값처럼 보이면 안 됩니다.'
    ];

    // ---- 작은 도구들 ------------------------------------------------------
    function svgEl(name, attrs, parent) {
        var e = document.createElementNS(NS, name);
        if (attrs) {
            for (var k in attrs) {
                if (Object.prototype.hasOwnProperty.call(attrs, k)) e.setAttribute(k, attrs[k]);
            }
        }
        if (parent) parent.appendChild(e);
        return e;
    }

    function textEl(parent, x, y, cls, anchor) {
        return svgEl('text', { x: x, y: y, 'class': cls, 'text-anchor': anchor || 'start' }, parent);
    }

    function setText(el, s) {
        if (el) el.textContent = (s === null || s === undefined) ? '' : String(s);
    }

    function setClass(el, cls) {
        if (el) el.setAttribute('class', cls);
    }

    // 값이 아예 없는 것과, 키는 있는데 숫자가 아닌 것(NaN, null, 문자열)을 똑같이 취급합니다.
    // 둘 다 "이 채널의 지금 값을 모른다" 이고, 모르는 것을 0 으로 그리지 않는 것이 규칙입니다.
    function pick(state, id) {
        if (!state || !id) return null;
        var e = state[id];
        if (!e || typeof e.value !== 'number' || !isFinite(e.value)) return null;
        return e;
    }

    function isStale(e, now) {
        return !!e && typeof e.at === 'number' && isFinite(e.at) && (now - e.at) > STALE_MS;
    }

    function ageText(ms) {
        var s = Math.round(ms / 1000);
        if (s < 0) s = 0;
        if (s < 60) return s + '초 전';
        return Math.floor(s / 60) + '분 전';
    }

    // W 는 kW 로 접습니다. 자릿수를 줄이는 표기 변환일 뿐, 다른 단위가 오면 손대지 않습니다.
    function fmtPower(v, unit) {
        if (unit === 'W') {
            var a = Math.abs(v);
            if (a >= 1000) return (v / 1000).toFixed(2) + ' kW';
            return v.toFixed(a >= 100 ? 0 : 1) + ' W';
        }
        return v.toFixed(2) + (unit ? ' ' + unit : '');
    }

    // ---- 상태 -------------------------------------------------------------
    var svgRoot = null;
    var ui = null;
    var lastState = null;
    var running = false, lastTs = 0, lastRecheck = 0;

    var raf = (root && typeof root.requestAnimationFrame === 'function')
        ? function (cb) { return root.requestAnimationFrame(cb); }
        : null;

    // ---- 화면 만들기 ------------------------------------------------------
    function pill(parent, cx, top, label) {
        var g = svgEl('g', { 'class': 'pf-pill' }, parent);
        svgEl('rect', { x: cx - 19, y: top, width: 38, height: 14, rx: 3, 'class': 'pf-pill-bg' }, g);
        var t = textEl(g, cx, top + 10, 'pf-pill-t', 'middle');
        setText(t, label);
        return g;
    }

    function buildStage(svg, def, index) {
        var bx = MARGIN + index * (BOX_W + GAP);
        var g = svgEl('g', null, svg);

        var box = svgEl('rect', { x: bx, y: BOX_Y, width: BOX_W, height: BOX_H, rx: 12, 'class': 'pf-box' }, g);
        var tint = svgEl('rect', { x: bx, y: BOX_Y, width: BOX_W, height: BOX_H, rx: 12, 'class': 'pf-tint' }, g);

        var name = textEl(g, bx + PAD, BOX_Y + 22, 'pf-name', 'start');
        setText(name, def.name);
        var badge = textEl(g, bx + BOX_W - PAD, BOX_Y + 22, 'pf-badge', 'end');
        var sub = textEl(g, bx + PAD, BOX_Y + 38, 'pf-sub pf-mono', 'start');
        setText(sub, def.tag);
        svgEl('line', { x1: bx + PAD, y1: BOX_Y + 48, x2: bx + BOX_W - PAD, y2: BOX_Y + 48, 'class': 'pf-div' }, g);

        var top = BOX_Y + 52;
        var area = BOX_H - 60;
        var y0 = top + (area - def.rows.length * ROW_H) / 2;

        var rows = [];
        for (var j = 0; j < def.rows.length; j++) {
            var r = def.rows[j];
            var ry = y0 + j * ROW_H;
            var rg = svgEl('g', { 'class': 'pf-row' }, g);
            var num = textEl(rg, bx + PAD, ry + 22, 'pf-num', 'start');
            var numT = svgEl('tspan', null, num);
            var unitT = svgEl('tspan', { dx: 4, 'class': 'pf-unit' }, num);
            var status = textEl(rg, bx + BOX_W - PAD, ry + 22, 'pf-status', 'end');
            var idT = textEl(rg, bx + PAD, ry + 38, 'pf-id pf-mono', 'start');
            setText(idT, r.id);
            setText(svgEl('title', null, rg), r.label);
            rows.push({ def: r, g: rg, num: num, numT: numT, unitT: unitT, status: status });
        }

        return { def: def, box: box, tint: tint, name: name, badge: badge, rows: rows };
    }

    function buildLink(svg, def, index) {
        var x0 = MARGIN + index * (BOX_W + GAP) + BOX_W + 10;
        var x1 = MARGIN + (index + 1) * (BOX_W + GAP) - 10;
        var cx = (x0 + x1) / 2;

        var g = svgEl('g', { 'class': 'pf-link' }, svg);
        var rail = svgEl('line', { x1: x0, y1: MID, x2: x1, y2: MID, 'class': 'pf-rail' }, g);
        var flow = svgEl('line', { x1: x0, y1: MID, x2: x1 - 14, y2: MID, 'class': 'pf-flow' }, g);
        var arrow = svgEl('path', { 'class': 'pf-arrow' }, g);

        var power = textEl(g, cx, MID - 30, 'pf-power', 'middle');
        var cap = textEl(g, cx, MID - 17, 'pf-cap pf-mono', 'middle');

        var L = {
            def: def, x0: x0, x1: x1, cx: cx, g: g, rail: rail, flow: flow, arrow: arrow,
            power: power, cap: cap, pill: null, speed: 0, offset: 0,
            effG: null, eff: null, effPill: null, effCap: null, over: null,
            ratio: null, ratioText: null, ratioNum: null, ratioMark: null, ratioCap: null
        };

        // 전력 채널이 있는 링크에만 '계산값' 표를 답니다. 전력은 어떤 장비도 보고하지 않는
        // 값이므로, 측정값과 같은 무게로 읽히면 안 됩니다.
        if (def.power) L.pill = pill(g, cx, MID + 13, '계산값');

        if (def.badges) {
            var eg = svgEl('g', null, g);
            L.effG = eg;
            L.eff = textEl(eg, cx, MID + 16, 'pf-eff', 'middle');
            L.effPill = pill(eg, cx, MID + 22, '계산값');
            L.effCap = textEl(eg, cx, MID + 46, 'pf-cap pf-mono', 'middle');
            L.over = textEl(eg, cx, MID + 58, 'pf-over', 'middle');

            var rg = svgEl('g', null, g);
            L.ratio = rg;
            // 숫자와 '계산값' 표를 각각 tspan 에 담습니다. <text> 에 직접 textContent 를 쓰면
            // 자식 tspan 이 통째로 지워지고, 표는 첫 갱신에서 조용히 사라집니다.
            L.ratioText = textEl(rg, cx, MID + 72, 'pf-ratio', 'middle');
            L.ratioNum = svgEl('tspan', null, L.ratioText);
            L.ratioMark = svgEl('tspan', { dx: 5, 'class': 'pf-mark' }, L.ratioText);
            L.ratioCap = textEl(rg, cx, MID + 86, 'pf-cap pf-mono', 'middle');
        }

        return L;
    }

    function build(container) {
        var svg = svgEl('svg', {
            viewBox: '0 0 ' + W + ' ' + H,
            preserveAspectRatio: 'xMidYMid meet',
            width: '100%',
            role: 'img',
            'aria-label': '전력 흐름도',
            style: 'display:block;width:100%;height:auto'
        });
        setText(svgEl('title', null, svg), '계통에서 서버 부하까지의 전력 흐름');
        setText(svgEl('style', null, svg), CSS);

        var stages = [], links = [], i;
        // 링크를 먼저 그려야 상자가 그 위에 얹힙니다. 굵은 흐름선이 상자 모서리를 파고드는
        // 것을 z-order 로 막습니다.
        for (i = 0; i < LINKS.length; i++) links.push(buildLink(svg, LINKS[i], i));
        for (i = 0; i < STAGES.length; i++) stages.push(buildStage(svg, STAGES[i], i));

        svgEl('line', { x1: MARGIN, y1: 200, x2: W - MARGIN, y2: 200, 'class': 'pf-div' }, svg);
        for (i = 0; i < LEGEND.length; i++) {
            setText(textEl(svg, MARGIN, 214 + i * 18, 'pf-legend', 'start'), LEGEND[i]);
        }

        container.appendChild(svg);
        svgRoot = svg;
        ui = { stages: stages, links: links };
    }

    // ---- 그리기 -----------------------------------------------------------
    function arrowPath(L, dir) {
        var tip = dir < 0 ? L.x0 : L.x1;
        var back = dir < 0 ? L.x0 + 13 : L.x1 - 13;
        return 'M' + back + ',' + (MID - 7) + 'L' + tip + ',' + MID + 'L' + back + ',' + (MID + 7) + 'Z';
    }

    function renderStage(S, state, now) {
        var breach = false;

        for (var j = 0; j < S.rows.length; j++) {
            var r = S.rows[j];
            var e = pick(state, r.def.id);

            if (!e) {
                // 0 이 아니라 — 입니다. 여기에 0 을 쓰면 보고된 적 없는 채널과 정말 0 V 인
                // 채널이 화면에서 같아집니다.
                setText(r.numT, '—');
                setText(r.unitT, '');
                setClass(r.num, 'pf-num pf-absent');
                setText(r.status, '값 없음');
                setClass(r.g, 'pf-row');
                continue;
            }

            var stale = isStale(e, now);
            var bad = e.limitBreach === true;
            if (bad) breach = true;

            setText(r.numT, e.value.toFixed(r.def.dp));
            setText(r.unitT, e.unit || r.def.unit);
            setClass(r.num, 'pf-num' + (bad ? ' pf-alarm' : ''));
            // 오래됨이 계산값 표시보다 우선합니다. 멈춘 값이라는 사실이 그 값의 출처보다
            // 급한 정보이고, 자리는 한 칸뿐입니다.
            setText(r.status, stale ? ageText(now - e.at) : (e.derived === true ? '계산값' : ''));
            // 상자 전체가 아니라 줄 단위로 흐리게 합니다. 채널마다 따로 조용해지므로,
            // 상자 하나가 통째로 흐려지면 어느 채널이 끊겼는지 알 수 없습니다.
            setClass(r.g, 'pf-row' + (stale ? ' pf-stale' : ''));
        }

        setClass(S.box, 'pf-box' + (breach ? ' pf-alarm' : ''));
        setClass(S.tint, 'pf-tint' + (breach ? ' pf-on' : ''));
        setClass(S.name, 'pf-name' + (breach ? ' pf-alarm' : ''));
        setText(S.badge, breach ? '한계 초과' : '');
    }

    function renderLink(L, state, now) {
        var def = L.def;
        var e = def.power ? pick(state, def.power) : null;

        if (!e) {
            setText(L.power, '값 없음');
            setClass(L.power, 'pf-power pf-absent');
            // 없는 이유를 구분합니다. "이 구간을 재는 채널이 아예 없다" 와 "채널은 있는데
            // 아직 한 번도 안 왔다" 는 운영자가 확인해야 할 곳이 서로 다릅니다.
            setText(L.cap, def.power ? (def.power + ' · 수신 없음') : def.noChannel);
            setClass(L.rail, 'pf-rail pf-unknown');
            setClass(L.flow, 'pf-flow pf-hide');
            setClass(L.arrow, 'pf-arrow pf-unknown');
            L.arrow.setAttribute('d', arrowPath(L, 1));
            setClass(L.g, 'pf-link');
            if (L.pill) setClass(L.pill, 'pf-pill pf-hide');
            L.speed = 0;
            return;
        }

        var stale = isStale(e, now);
        var bad = e.limitBreach === true;
        var mag = Math.abs(e.value);
        var frac = Math.min(1, mag / FULL_SCALE_W);
        // DAB 는 양방향입니다. 음수 전력을 오른쪽 화살표로 그리면 방향을 거짓말하는 것이므로
        // 화살표와 파선을 뒤집습니다. 숫자는 부호까지 받은 그대로 씁니다.
        var dir = e.value < 0 ? -1 : 1;

        setText(L.power, fmtPower(e.value, e.unit || def.unit));
        setClass(L.power, 'pf-power' + (bad ? ' pf-alarm' : ''));
        setText(L.cap, def.power + (stale ? ' · ' + ageText(now - e.at) : ''));
        setClass(L.rail, 'pf-rail');
        setClass(L.flow, 'pf-flow' + (bad ? ' pf-alarm' : ''));
        L.flow.setAttribute('stroke-width', (3 + 9 * frac).toFixed(1));
        L.flow.setAttribute('x1', dir < 0 ? L.x0 + 14 : L.x0);
        L.flow.setAttribute('x2', dir < 0 ? L.x1 : L.x1 - 14);
        setClass(L.arrow, 'pf-arrow' + (bad ? ' pf-alarm' : ''));
        L.arrow.setAttribute('d', arrowPath(L, dir));
        setClass(L.g, 'pf-link' + (stale ? ' pf-stale' : ''));
        if (L.pill) setClass(L.pill, 'pf-pill');

        // 정확히 0 W 는 살아 있지만 멈춘 선입니다. 오래된 값도 멈춥니다 — 갱신이 끊긴 링크가
        // 계속 흐르고 있으면, 그 움직임 자체가 지금 전력이 흐르고 있다는 거짓 신호가 됩니다.
        L.speed = (stale || mag === 0) ? 0 : (18 + 130 * frac) * dir;
    }

    function renderBadges(L, state, now) {
        if (!L.effG) return;

        var e = pick(state, 'psfb.efficiency');
        if (!e) {
            // 효율이 없으면 p_out / p_in 을 여기서 나누지 않습니다. 두 값은 서로 다른 순간의
            // 것일 수 있고, 그렇게 얻은 비율은 어느 순간에도 성립한 적이 없는 숫자입니다.
            setText(L.eff, '효율 값 없음');
            setClass(L.eff, 'pf-eff pf-absent');
            setText(L.effCap, 'psfb.efficiency · 수신 없음');
            setText(L.over, '');
            setClass(L.effPill, 'pf-pill pf-hide');
            setClass(L.effG, '');
        } else {
            var stale = isStale(e, now);
            setText(L.eff, '효율 ' + e.value.toFixed(1) + ' ' + (e.unit || '%'));
            setClass(L.eff, 'pf-eff' + (e.limitBreach === true ? ' pf-alarm' : ''));
            setText(L.effCap, 'psfb.efficiency' + (stale ? ' · ' + ageText(now - e.at) : ''));
            // 100 % 를 넘겨도 자르지 않고, 숨기지도 않습니다. 시뮬레이터는 채널을 일부러
            // 서로 독립적으로 흔들기 때문에 초과가 정상적으로 나오며, 그것은 데이터의 성질
            // 자체입니다. 100 으로 눌러 그리면 화면은 그럴듯해지고 그 성질은 사라집니다.
            setText(L.over, e.value > 100 ? '100 % 초과 · 보정 없음' : '');
            setClass(L.effPill, 'pf-pill');
            setClass(L.effG, stale ? 'pf-stale' : '');
        }

        var r = pick(state, 'psfb.conversion_ratio');
        if (!r) {
            setText(L.ratioNum, '변환비 값 없음');
            setClass(L.ratioText, 'pf-ratio pf-absent');
            setText(L.ratioMark, '');
            setText(L.ratioCap, 'psfb.conversion_ratio · 수신 없음');
            setClass(L.ratio, '');
        } else {
            var rs = isStale(r, now);
            setText(L.ratioNum, '변환비 ' + r.value.toFixed(3));
            setClass(L.ratioText, 'pf-ratio' + (r.limitBreach === true ? ' pf-alarm' : ''));
            setText(L.ratioMark, '계산값');
            setText(L.ratioCap, 'psfb.conversion_ratio' + (rs ? ' · ' + ageText(now - r.at) : ''));
            setClass(L.ratio, rs ? 'pf-stale' : '');
        }
    }

    function render(state, now) {
        if (!ui) return;
        var i;
        for (i = 0; i < ui.stages.length; i++) renderStage(ui.stages[i], state, now);
        for (i = 0; i < ui.links.length; i++) {
            renderLink(ui.links[i], state, now);
            renderBadges(ui.links[i], state, now);
        }
    }

    // ---- 애니메이션 -------------------------------------------------------
    // requestAnimationFrame 이 없는 환경(검증 하니스의 DOM 스텁 등)에서는 그냥 움직이지
    // 않습니다. 움직임은 읽기를 돕는 장식이고, 숫자와 '값 없음' 이 이 파일의 본체입니다.
    function frame(ts) {
        if (!svgRoot || !raf) { running = false; return; }
        var t = (typeof ts === 'number' && isFinite(ts)) ? ts : 0;
        var dt = lastTs ? Math.min(0.25, (t - lastTs) / 1000) : 0;
        lastTs = t;

        for (var i = 0; i < ui.links.length; i++) {
            var L = ui.links[i];
            if (!L.speed) continue;
            L.offset = (L.offset - L.speed * dt) % DASH;
            L.flow.setAttribute('stroke-dashoffset', L.offset.toFixed(2));
        }

        // 값이 새로 오지 않아도 시간은 흐릅니다. 호스트가 조용해진 화면이 계속 '방금 값'
        // 처럼 보이면 안 되므로, update 를 기다리지 않고 여기서 4 Hz 로 나이를 다시 셉니다.
        if (lastState && (t - lastRecheck) > 250) {
            lastRecheck = t;
            render(lastState, Date.now());
        }

        raf(frame);
    }

    function startAnim() {
        if (running || !raf) return;
        running = true;
        lastTs = 0;
        lastRecheck = 0;
        raf(frame);
    }

    // ---- 공개 API ---------------------------------------------------------
    function mount(container) {
        if (!container || typeof container.appendChild !== 'function') return;
        if (svgRoot && svgRoot.parentNode && typeof svgRoot.parentNode.removeChild === 'function') {
            svgRoot.parentNode.removeChild(svgRoot);
        }
        build(container);
        // 데이터가 오기 전에도 화면은 완결되어 있어야 합니다: 전부 '값 없음' 인 그림은
        // 빈 화면이 아니라 "아직 아무것도 도착하지 않았다" 는 정확한 보고입니다.
        render(lastState || {}, Date.now());
        startAnim();
    }

    function update(state) {
        if (!ui) return;   // mount 전에 불려도 조용히 넘어갑니다.
        lastState = (state && typeof state === 'object') ? state : {};
        render(lastState, Date.now());
    }

    root.PowerFlow = { mount: mount, update: update };
})(typeof window !== 'undefined' ? window : this);
