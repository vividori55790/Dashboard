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

    모양은 T 자입니다. 위쪽 가로줄이 평상시 급전 경로(계통 -> DC 버스 -> PSFB -> 서버)이고,
    DC 버스에서 아래로 내려가는 기둥 끝에 UPS 가지가 병렬로 붙습니다. 이것이 온라인 이중변환
    UPS 의 실제 결선이고, 그림을 그렇게 그리는 이유는 정전 시나리오가 무엇을 하는지가 배치
    자체에서 읽히기 때문입니다: 위쪽 왼쪽 끝이 0 V 가 되어도 버스 아래에 붙은 가지가 버스를
    떠받치고 있으면 오른쪽 절반은 계속 살아 있습니다.

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
    // H 는 아래에서 범례 줄 수까지 세어 계산합니다. 손으로 적어 두었더니 범례를 한 줄 늘린
    // 날 마지막 줄이 viewBox 밖으로 나갔고, SVG 는 그것을 오류로 알리지 않고 그냥 자릅니다.
    var W = 1160, H = 0;
    var MARGIN = 16, BOX_W = 168, BOX_H = 150, BOX_Y = 30, GAP = 152;
    var ROW_GAP = 96;              // 위쪽 가로줄과 아래쪽 UPS 가지 사이, T 의 기둥이 지나는 높이
    var PAD = 14;                  // 상자 안쪽 여백
    var ROW_H = 44;                // 값 한 줄(숫자 + 채널 id)이 차지하는 높이

    var STALE_MS = 10000;          // dab_psfb_console.html 과 같은 기준
    var DASH = 24;                 // 파선 한 주기 (14 + 10). 오프셋을 이 값으로 접습니다.

    // 격자. 상자는 열과 행으로만 위치를 말하고, 실제 좌표는 여기서 한 번만 계산합니다. 좌표를
    // 상자마다 손으로 적으면 한 칸을 옮길 때 그 칸에 붙은 선이 따라오지 않습니다.
    function colX(col) { return MARGIN + col * (BOX_W + GAP); }
    function rowY(row) { return BOX_Y + row * (BOX_H + ROW_GAP); }
    function colCx(col) { return colX(col) + BOX_W / 2; }
    function rowCy(row) { return rowY(row) + BOX_H / 2; }

    // 굵기와 속도를 정하려면 전력의 눈금이 필요합니다. 이 값은 측정값이 아니라 그리기 눈금이며,
    // 프로파일이 선언한 범위에서 옵니다: DAB 450 V x 40 A = 18 kW, PSFB 54 V x 260 A = 14 kW,
    // 배터리 58 V x 220 A = 12.8 kW. 화면에 들어온 최대값으로 자동 조정하지 않는 이유는, 그러면
    // 한 링크의 굵기가 다른 링크의 값에 따라 변하기 때문입니다 — 같은 전력이 시각마다 다른
    // 굵기로 보이는 쪽이 더 나쁩니다.
    var FULL_SCALE_W = 18000;

    // 단계. id 는 프레임에 실제로 실려 오는 문자열이므로 정확히 일치시킵니다. 부분 문자열로
    // 맞추면 grid.voltage 와 psfb.output_voltage 가 서로의 칸을 덮어씁니다 — 이 프로젝트가
    // 이미 한 번 겪은 결함입니다.
    var STAGES = [
        { name: '계통', tag: 'grid', col: 0, row: 0, rows: [
            { id: 'grid.voltage', unit: 'V', dp: 0, label: '계통 전압' }
        ] },
        // 이 상자가 DC 버스입니다. 이름을 '배터리 컨버터' 에서 바꾼 이유는 아래쪽에 진짜 배터리
        // 가지가 생겼기 때문입니다. 상자 두 개가 같은 것을 가리키는 이름을 달고 있으면, 어느
        // 쪽 숫자를 읽고 있는지 화면에서 구분할 수 없습니다. 채널 id 는 그대로 dab.* 입니다.
        { name: 'DAB 컨버터 · DC 버스', tag: 'dab', col: 1, row: 0, rows: [
            { id: 'dab.bus_voltage', unit: 'V', dp: 0, label: 'DAB 출력 버스 전압' },
            { id: 'dab.input_current', unit: 'A', dp: 2, label: 'DAB 입력 전류' }
        ] },
        { name: 'PSFB 48 V 레일', tag: 'psfb', col: 2, row: 0, rows: [
            { id: 'psfb.output_voltage', unit: 'V', dp: 2, label: 'PSFB 출력 전압' },
            { id: 'psfb.output_current', unit: 'A', dp: 1, label: 'PSFB 출력 전류' }
        ] },
        { name: '서버 부하', tag: 'server', col: 3, row: 0, rows: [
            { id: 'server.load', unit: '%', dp: 1, label: '서버 부하율' }
        ] },

        // T 의 기둥 끝. 전류는 컨버터가 흘리는 것이고 전압과 충전율은 뱅크의 성질이므로 상자를
        // 나눕니다. 한 상자에 몰아 넣으면 그 셋이 같은 곳에서 측정된 것처럼 읽힙니다.
        { name: 'UPS DAB 컨버터 (병렬)', tag: 'ups', col: 1, row: 1, rows: [
            { id: 'ups.bus_current', unit: 'A', dp: 2, label: 'UPS 버스측 전류 (양수 = 버스로 나감)' },
            { id: 'ups.battery_current', unit: 'A', dp: 1, label: 'UPS 배터리 전류 (양수 = 충전)' }
        ] },
        { name: '배터리 뱅크', tag: 'ups.battery', col: 2, row: 1, rows: [
            { id: 'ups.battery_voltage', unit: 'V', dp: 2, label: '배터리 단자 전압' },
            { id: 'ups.state_of_charge', unit: '%', dp: 1, label: '배터리 충전율 (전류의 적분)' }
        ] }
    ];

    // 링크. 가운데 구간에 전력 채널이 없는 것은 버그가 아니라 이 장비의 사실입니다. 호스트가
    // 계산하는 전력은 dab.p_in, psfb.p_out, ups.p_batt 셋뿐이고, DAB 와 PSFB 사이를 재는 채널은
    // 선언된 적이 없습니다. 그래서 p_in 을 두 링크에 겹쳐 그리지 않습니다 — 같은 전력이 두
    // 구간을 그대로 흐른다고 말하는 것이 되고, 그것은 손실이 0 이라는, 아무도 측정하지 않은
    // 주장입니다.
    //
    // p_in 을 첫 링크(계통 -> DAB)에 두는 근거는 채널 이름 자체입니다: DAB 로 들어가는 전력.
    // 다만 그 식은 dab.bus_voltage * dab.input_current 로, 출력측 전압과 입력측 전류를 곱한
    // 것입니다. 그래서 값 밑에 항상 채널 id 를 같이 적습니다 — 이 그림이 붙인 이름이 아니라
    // 호스트가 선언한 채널이라는 것을 읽는 사람이 확인할 수 있어야 합니다.
    //
    // 같은 규칙이 T 의 기둥에도 그대로 적용됩니다. ups.p_batt 는 배터리 단자에서의 전력이므로
    // 컨버터와 뱅크 사이에만 그립니다. 버스와 컨버터 사이에 겹쳐 그리면 변환 손실이 0 이라고
    // 말하는 것이 되고, 그 구간을 재는 채널은 선언된 적이 없습니다.
    var LINKS = [
        { from: [0, 0], to: [1, 0], power: 'dab.p_in', unit: 'W' },
        { from: [1, 0], to: [2, 0], power: null, noChannel: '구간 전력 채널 없음', badges: true },
        { from: [2, 0], to: [3, 0], power: 'psfb.p_out', unit: 'W' },

        // T 의 기둥. 방향이 아래에서 위입니다 — UPS 가지가 버스를 떠받치는 쪽이 양수이므로,
        // 정전 중에는 화살표가 위쪽 가로줄을 향해 올라갑니다. from 을 아래 상자로 둔 것이
        // 그 부호 규약을 좌표로 옮긴 것이고, 그래서 부호가 뒤집히면 화살표도 뒤집힙니다.
        { from: [1, 1], to: [1, 0], vertical: true, power: 'ups.p_bus', unit: 'W',
          signLabels: { positive: '버스 지원', negative: '버스에서 충전' } },

        // 부호를 읽는 유일한 곳입니다. 프로파일이 "양수는 뱅크로 들어가는 전력" 이라고 선언하고
        // 있고, 배터리 상자가 오른쪽에 있으므로 양수가 오른쪽 화살표가 됩니다 — 이 파일이 지어낸
        // 뜻이 아니라 선언된 부호 규약을 글자로 옮긴 것이므로 데이터로 적어 둡니다.
        { from: [1, 1], to: [2, 1], power: 'ups.p_batt', unit: 'W',
          signLabels: { positive: '충전', negative: '방전' } }
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
        '.pf-mode{fill:var(--text-2);font-size:11px;font-weight:600}',
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
        '10초 넘게 소식이 없는 값은 흐리게 표시하고 경과 시간을 씁니다 — 멈춘 값이 살아 있는 값처럼 보이면 안 됩니다.',
        'UPS 가지는 DC 버스에 병렬로 붙습니다. ups.p_batt 는 부호 있는 값이고, 양수는 뱅크로 들어가는 전력(충전), 음수는 뱅크가 버스를 떠받치는 전력(방전)입니다.',
        '버스와 UPS 컨버터 사이는 재는 채널이 없어 비워 둡니다. 배터리 단자 전력을 그 구간에 겹쳐 그리면 변환 손실이 0 이라고 주장하는 것이 됩니다.',
        '충전율은 전류를 시간으로 적분한 값입니다. 방전 중에만 내려가고, 스스로 흔들리지 않습니다 — 흔들리는 충전율은 측정값처럼 보이면서 아무 뜻도 없습니다.'
    ];

    // 범례가 시작하는 높이와, 그로부터 정해지는 그림 전체의 높이. 두 값을 따로 적어 두면
    // 범례 한 줄이 viewBox 밖으로 나가고, SVG 는 그것을 잘라 낼 뿐 아무것도 알리지 않습니다.
    var LEGEND_TOP = rowY(1) + BOX_H + 24;
    var LEGEND_LINE = 18;
    H = LEGEND_TOP + 16 + LEGEND.length * LEGEND_LINE + 8;

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

    function setLine(el, ax, ay, bx, by) {
        el.setAttribute('x1', ax);
        el.setAttribute('y1', ay);
        el.setAttribute('x2', bx);
        el.setAttribute('y2', by);
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

    function buildStage(svg, def) {
        var bx = colX(def.col);
        var by = rowY(def.row);
        var g = svgEl('g', null, svg);

        var box = svgEl('rect', { x: bx, y: by, width: BOX_W, height: BOX_H, rx: 12, 'class': 'pf-box' }, g);
        var tint = svgEl('rect', { x: bx, y: by, width: BOX_W, height: BOX_H, rx: 12, 'class': 'pf-tint' }, g);

        var name = textEl(g, bx + PAD, by + 22, 'pf-name', 'start');
        setText(name, def.name);
        var badge = textEl(g, bx + BOX_W - PAD, by + 22, 'pf-badge', 'end');
        var sub = textEl(g, bx + PAD, by + 38, 'pf-sub pf-mono', 'start');
        setText(sub, def.tag);
        svgEl('line', { x1: bx + PAD, y1: by + 48, x2: bx + BOX_W - PAD, y2: by + 48, 'class': 'pf-div' }, g);

        var top = by + 52;
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

    /// 링크 하나의 양 끝점과, 글자를 어디에 놓을지.
    ///
    /// 가로줄과 T 의 기둥이 같은 코드를 씁니다. 세로용 그리기 함수를 따로 두면 한쪽만 고쳐지는
    /// 날이 오고, 그 날 화면에는 서로 다른 규칙으로 그려진 두 종류의 선이 함께 있게 됩니다.
    function geometry(def) {
        if (def.vertical) {
            var vx = colCx(def.from[0]);
            // 아래에서 위로 가는 링크가 있으므로 어느 쪽이 위인지 좌표에서 정합니다. 항상
            // 위에서 아래로 그린다고 가정하면 T 의 기둥이 상자 안쪽에서 시작해 버립니다.
            var down = def.to[1] > def.from[1];
            var ay = down ? rowY(def.from[1]) + BOX_H + 8 : rowY(def.from[1]) - 8;
            var by = down ? rowY(def.to[1]) - 8 : rowY(def.to[1]) + BOX_H + 8;
            return {
                ax: vx, ay: ay, bx: vx, by: by, vertical: true,
                // 세로선은 글자를 옆에 세웁니다. 선 위아래에 놓으면 상자에 붙습니다.
                lx: vx + 16, ly: (ay + by) / 2, anchor: 'start'
            };
        }

        var x0 = colX(def.from[0]) + BOX_W + 10;
        var x1 = colX(def.to[0]) - 10;
        var y = rowCy(def.from[1]);
        return {
            ax: x0, ay: y, bx: x1, by: y, vertical: false,
            lx: (x0 + x1) / 2, ly: y, anchor: 'middle'
        };
    }

    function buildLink(svg, def) {
        var G = geometry(def);
        var g = svgEl('g', { 'class': 'pf-link' }, svg);

        var rail = svgEl('line', { 'class': 'pf-rail' }, g);
        setLine(rail, G.ax, G.ay, G.bx, G.by);
        var flow = svgEl('line', { 'class': 'pf-flow' }, g);
        var arrow = svgEl('path', { 'class': 'pf-arrow' }, g);

        // 세로선은 글자가 선 옆으로 가므로 기준선이 달라집니다. 가로선은 지금까지처럼 선 위에
        // 쌓고, 세로선은 가운데 높이에서 아래로 흘립니다.
        // 18 px 간격입니다. 14 로 두었더니 11 px 짜리 방향 글자와 15 px 짜리 전력 숫자가
        // 브라우저에서 4 px 겹쳤습니다 -- DOM 스텁은 글자 크기를 모르므로 잡지 못하고,
        // 실제로 띄워 상자를 재어야만 보입니다.
        var top = G.vertical ? G.ly - 18 : G.ly - 48;

        var mode = textEl(g, G.lx, top, 'pf-mode', G.anchor);
        var power = textEl(g, G.lx, top + 18, 'pf-power', G.anchor);
        var cap = textEl(g, G.lx, top + 31, 'pf-cap pf-mono', G.anchor);

        var L = {
            def: def, G: G, g: g, rail: rail, flow: flow, arrow: arrow,
            mode: mode, power: power, cap: cap, pill: null, speed: 0, offset: 0,
            effG: null, eff: null, effPill: null, effCap: null, over: null,
            ratio: null, ratioText: null, ratioNum: null, ratioMark: null, ratioCap: null
        };

        // 전력 채널이 있는 링크에만 '계산값' 표를 답니다. 전력은 어떤 장비도 보고하지 않는
        // 값이므로, 측정값과 같은 무게로 읽히면 안 됩니다.
        //
        // 가로선에서는 표를 선 아래에 답니다. 위쪽 글자 더미에 이어 붙이면 표가 선 위로 올라와
        // 굵은 흐름선을 덮습니다 — 배경이 칠해진 도형이라 선이 끊긴 것처럼 보입니다.
        if (def.power) {
            L.pill = pill(g, G.vertical ? G.lx + 19 : G.lx,
                          G.vertical ? top + 40 : G.ly + 13, '계산값');
        }

        if (def.badges) {
            var cx = G.lx, mid = G.ly;
            var eg = svgEl('g', null, g);
            L.effG = eg;
            L.eff = textEl(eg, cx, mid + 16, 'pf-eff', 'middle');
            L.effPill = pill(eg, cx, mid + 22, '계산값');
            L.effCap = textEl(eg, cx, mid + 46, 'pf-cap pf-mono', 'middle');
            L.over = textEl(eg, cx, mid + 58, 'pf-over', 'middle');

            var rg = svgEl('g', null, g);
            L.ratio = rg;
            // 숫자와 '계산값' 표를 각각 tspan 에 담습니다. <text> 에 직접 textContent 를 쓰면
            // 자식 tspan 이 통째로 지워지고, 표는 첫 갱신에서 조용히 사라집니다.
            L.ratioText = textEl(rg, cx, mid + 72, 'pf-ratio', 'middle');
            L.ratioNum = svgEl('tspan', null, L.ratioText);
            L.ratioMark = svgEl('tspan', { dx: 5, 'class': 'pf-mark' }, L.ratioText);
            L.ratioCap = textEl(rg, cx, mid + 86, 'pf-cap pf-mono', 'middle');
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
        setText(svgEl('title', null, svg), '계통에서 서버 부하까지의 전력 흐름과, DC 버스에 병렬로 붙은 UPS 가지');
        setText(svgEl('style', null, svg), CSS);

        var stages = [], links = [], i;
        // 링크를 먼저 그려야 상자가 그 위에 얹힙니다. 굵은 흐름선이 상자 모서리를 파고드는
        // 것을 z-order 로 막습니다.
        for (i = 0; i < LINKS.length; i++) links.push(buildLink(svg, LINKS[i]));
        for (i = 0; i < STAGES.length; i++) stages.push(buildStage(svg, STAGES[i]));

        svgEl('line', { x1: MARGIN, y1: LEGEND_TOP, x2: W - MARGIN, y2: LEGEND_TOP, 'class': 'pf-div' }, svg);
        for (i = 0; i < LEGEND.length; i++) {
            setText(textEl(svg, MARGIN, LEGEND_TOP + 16 + i * LEGEND_LINE, 'pf-legend', 'start'), LEGEND[i]);
        }

        container.appendChild(svg);
        svgRoot = svg;
        ui = { stages: stages, links: links };
    }

    // ---- 그리기 -----------------------------------------------------------
    // a 에서 b 로 향하는 단위 벡터. 가로/세로/어느 방향이든 같은 식으로 다룹니다 -- 방향마다
    // 분기를 두면 나중에 추가되는 방향은 그 분기 중 하나를 빠뜨린 채로 그려집니다.
    function unit(G) {
        var dx = G.bx - G.ax, dy = G.by - G.ay;
        var len = Math.sqrt(dx * dx + dy * dy) || 1;
        return [dx / len, dy / len];
    }

    // dir > 0 이면 a -> b, dir < 0 이면 b -> a.
    function arrowPath(G, dir) {
        var u = unit(G), ux = u[0] * dir, uy = u[1] * dir;
        var tipX = dir < 0 ? G.ax : G.bx;
        var tipY = dir < 0 ? G.ay : G.by;
        var backX = tipX - ux * 13, backY = tipY - uy * 13;
        var px = -uy * 7, py = ux * 7;            // 진행 방향에 수직인 반폭
        return 'M' + (backX + px).toFixed(1) + ',' + (backY + py).toFixed(1) +
               'L' + tipX.toFixed(1) + ',' + tipY.toFixed(1) +
               'L' + (backX - px).toFixed(1) + ',' + (backY - py).toFixed(1) + 'Z';
    }

    // 화살표가 차지하는 14 px 만큼 흐름선을 짧게 잘라, 파선이 화살촉을 뚫고 나가지 않게 합니다.
    function flowEnds(G, dir) {
        var u = unit(G), ux = u[0] * 14, uy = u[1] * 14;
        return dir < 0
            ? [G.ax + ux, G.ay + uy, G.bx, G.by]
            : [G.ax, G.ay, G.bx - ux, G.by - uy];
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
        var def = L.def, G = L.G;
        var e = def.power ? pick(state, def.power) : null;

        if (!e) {
            setText(L.mode, '');
            setText(L.power, '값 없음');
            setClass(L.power, 'pf-power pf-absent');
            // 없는 이유를 구분합니다. "이 구간을 재는 채널이 아예 없다" 와 "채널은 있는데
            // 아직 한 번도 안 왔다" 는 운영자가 확인해야 할 곳이 서로 다릅니다.
            setText(L.cap, def.power ? (def.power + ' · 수신 없음') : def.noChannel);
            setClass(L.rail, 'pf-rail pf-unknown');
            setClass(L.flow, 'pf-flow pf-hide');
            setClass(L.arrow, 'pf-arrow pf-unknown');
            L.arrow.setAttribute('d', arrowPath(G, 1));
            setClass(L.g, 'pf-link');
            if (L.pill) setClass(L.pill, 'pf-pill pf-hide');
            L.speed = 0;
            return;
        }

        var stale = isStale(e, now);
        var bad = e.limitBreach === true;
        var mag = Math.abs(e.value);
        var frac = Math.min(1, mag / FULL_SCALE_W);
        // DAB 도 UPS 도 양방향입니다. 음수 전력을 정방향 화살표로 그리면 방향을 거짓말하는
        // 것이므로 화살표와 파선을 뒤집습니다. 숫자는 부호까지 받은 그대로 씁니다.
        var dir = e.value < 0 ? -1 : 1;

        // 부호에 이름이 붙어 있는 링크에만 씁니다. 정확히 0 은 어느 쪽도 아니므로 비웁니다 —
        // 0 W 를 '충전' 이라고 쓰면 멈춘 것과 들어가는 것이 화면에서 같아집니다.
        setText(L.mode, def.signLabels && e.value !== 0
            ? (e.value > 0 ? def.signLabels.positive : def.signLabels.negative)
            : '');

        setText(L.power, fmtPower(e.value, e.unit || def.unit));
        setClass(L.power, 'pf-power' + (bad ? ' pf-alarm' : ''));
        setText(L.cap, def.power + (stale ? ' · ' + ageText(now - e.at) : ''));
        setClass(L.rail, 'pf-rail');
        setClass(L.flow, 'pf-flow' + (bad ? ' pf-alarm' : ''));
        L.flow.setAttribute('stroke-width', (3 + 9 * frac).toFixed(1));
        var ends = flowEnds(G, dir);
        setLine(L.flow, ends[0], ends[1], ends[2], ends[3]);
        setClass(L.arrow, 'pf-arrow' + (bad ? ' pf-alarm' : ''));
        L.arrow.setAttribute('d', arrowPath(G, dir));
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
