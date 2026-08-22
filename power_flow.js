/*
    power_flow.js — DAB/PSFB UPS 전력 계통의 실시간 단선도(one-line diagram).

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

    ── 배치 ────────────────────────────────────────────────────────────────

    고전압 DC 버스가 공통 모선이고, 변환기들은 그 모선에 서로 병렬로 매달립니다.

        [계통] ─▶ ═══════════ 고전압 DC 버스 ═══════════
                       │             │             │
                  dab.p_in ▼    ups.p_bus ▲     (채널 없음)
                       │             │             │
                 [DAB 컨버터]  [UPS DAB 컨버터]  [PSFB 48 V 레일]
                                     │             │
                              ups.p_batt ▼    psfb.p_out ▼
                               [배터리 뱅크]    [서버 부하]

    처음에는 이것을 계통 -> DAB -> PSFB -> 부하 의 한 줄짜리 사슬로 그렸습니다. 그것은 틀린
    그림이었습니다. DAB 와 PSFB 는 서로의 앞뒤에 있는 것이 아니라 같은 모선에 병렬로 붙어
    있고, 사슬로 그리면 PSFB 가 DAB 의 출력을 받는 것처럼 읽힙니다 — 실제로는 둘 다 모선에서
    가져갈 뿐이고, 한쪽이 멈춰도 모선이 살아 있는 한 다른 쪽은 계속 돕니다. 배치가 곧 주장
    이므로, 배치가 틀리면 숫자가 전부 맞아도 그림은 거짓말을 합니다.

    UPS 갈래의 화살표 방향이 이 그림에서 가장 많은 것을 말합니다. 배터리가 모선을 떠받치는
    동안에는 화살표가 모선을 향해 올라가고, 평상시 충전 중에는 내려옵니다. 그래서 정전
    시나리오가 무엇을 하는지가 숫자를 읽기 전에 배치에서 먼저 읽힙니다.

    DAB 상자 아래가 비어 있는 것도 사실입니다. 이 프로파일은 DAB 의 반대편을 재는 채널을
    선언하지 않으므로, 그 자리에 아무것도 그리지 않습니다 — 모르는 것을 그리지 않는 것이
    없는 값을 0 으로 적지 않는 것과 같은 규칙입니다.

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
    var MARGIN = 16;
    var COL_W = 186, COL_GAP = 128;
    var BOX_H = 140;               // 값 두 줄이 들어가는 높이
    var BUS_H = 64;                // 모선은 값 한 줄을 옆으로 눕혀 담습니다
    var PAD = 14;                  // 상자 안쪽 여백
    var ROW_H = 40;                // 값 한 줄(숫자 + 채널 id)이 차지하는 높이

    var ROW0_Y = 30;                          // 계통과 모선
    var ROW1_Y = ROW0_Y + BOX_H + 45;         // 모선에 병렬로 매달린 변환기들
    var ROW2_Y = ROW1_Y + BOX_H + 45;         // 그 변환기들이 물고 있는 것들
    var BUS_Y = ROW0_Y + (BOX_H - BUS_H) / 2; // 계통 상자와 세로 중앙을 맞춥니다

    var STALE_MS = 10000;          // dab_psfb_console.html 과 같은 기준
    var DASH = 24;                 // 파선 한 주기 (14 + 10). 오프셋을 이 값으로 접습니다.

    function colX(col) { return MARGIN + col * (COL_W + COL_GAP); }

    // 굵기와 속도를 정하려면 전력의 눈금이 필요합니다. 이 값은 측정값이 아니라 그리기 눈금이며,
    // 프로파일이 선언한 범위에서 옵니다: DAB 450 V x 40 A = 18 kW, PSFB 54 V x 260 A = 14 kW,
    // 배터리 58 V x 220 A = 12.8 kW. 화면에 들어온 최대값으로 자동 조정하지 않는 이유는, 그러면
    // 한 링크의 굵기가 다른 링크의 값에 따라 변하기 때문입니다 — 같은 전력이 시각마다 다른
    // 굵기로 보이는 쪽이 더 나쁩니다.
    var FULL_SCALE_W = 18000;

    // 상자. id 는 링크가 가리키는 이름이고, 채널 id 는 프레임에 실제로 실려 오는 문자열이므로
    // 정확히 일치시킵니다. 부분 문자열로 맞추면 grid.voltage 와 psfb.output_voltage 가 서로의
    // 칸을 덮어씁니다 — 이 프로젝트가 이미 한 번 겪은 결함입니다.
    var STAGES = [
        { id: 'grid', name: '계통', tag: 'grid · 상용 전력망', kind: 'source',
          x: colX(0), y: ROW0_Y, w: COL_W, h: BOX_H, rows: [
            { id: 'grid.voltage', unit: 'V', dp: 0, label: '계통 전압' }
        ] },

        // 공통 모선. 넓고 낮은 상자 하나로 그리는 이유는 이것이 한 지점이기 때문입니다 —
        // 아래로 내려가는 세 갈래는 서로 직렬이 아니라 전부 같은 노드에 붙어 있습니다.
        { id: 'bus', name: '고전압 DC 버스', tag: '공통 모선 · 세 갈래 병렬', kind: 'bus',
          compact: true,
          x: colX(1), y: BUS_Y, w: colX(3) + COL_W - colX(1), h: BUS_H, rows: [
            { id: 'dab.bus_voltage', unit: 'V', dp: 0, label: 'DC 버스 전압' }
        ] },

        { id: 'dab', name: 'DAB 컨버터', tag: 'dab · 모선에 병렬 · 반대편 미계측', kind: 'converter',
          x: colX(1), y: ROW1_Y, w: COL_W, h: BOX_H, rows: [
            { id: 'dab.input_current', unit: 'A', dp: 2, label: 'DAB 입력 전류' }
        ] },

        { id: 'ups', name: 'UPS DAB 컨버터', tag: 'ups · 모선에 병렬', kind: 'converter',
          x: colX(2), y: ROW1_Y, w: COL_W, h: BOX_H, rows: [
            { id: 'ups.bus_current', unit: 'A', dp: 2, label: 'UPS 모선측 전류 (양수 = 모선으로 나감)' },
            { id: 'ups.battery_current', unit: 'A', dp: 1, label: 'UPS 배터리 전류 (양수 = 충전)' }
        ] },

        { id: 'psfb', name: 'PSFB 48 V 레일', tag: 'psfb · 모선에 병렬', kind: 'rail',
          x: colX(3), y: ROW1_Y, w: COL_W, h: BOX_H, rows: [
            { id: 'psfb.output_voltage', unit: 'V', dp: 2, label: 'PSFB 출력 전압' },
            { id: 'psfb.output_current', unit: 'A', dp: 1, label: 'PSFB 출력 전류' }
        ] },

        // 효율과 변환비는 원래 두 상자 사이 링크에 매달려 있었습니다. 좁은 틈에 글자 일곱
        // 덩어리가 겹쳐 있었고, 그 틈에서 가장 큰 글자가 '값 없음' 이라 그림 한가운데가 고장난
        // 것처럼 보였습니다. 여기로 옮기면서 전용 그리기 코드도 같이 사라졌습니다 — 이것들은
        // 다른 채널과 똑같은 채널이므로 다른 채널과 똑같은 칸에 그리면 됩니다.
        //
        // 왼쪽 아래에 둔 것은 회로가 아니기 때문입니다. 변환기 바로 아래에 놓으면 선이 없어도
        // 그 변환기에 매달린 무언가로 읽힙니다.
        { id: 'derived', name: 'PSFB 변환 지표', tag: 'derived · 회로가 아님', kind: 'derived',
          x: colX(0), y: ROW2_Y, w: COL_W, h: BOX_H, rows: [
            { id: 'psfb.efficiency', unit: '%', dp: 1, label: 'PSFB 효율',
              over: 100, overNote: '100 % 초과 · 보정 없음' },
            { id: 'psfb.conversion_ratio', unit: '', dp: 3, label: 'PSFB 출력 / 모선 전압비' }
        ] },

        { id: 'battery', name: '배터리 뱅크', tag: 'ups.battery · 200 Ah', kind: 'storage',
          x: colX(2), y: ROW2_Y, w: COL_W, h: BOX_H, rows: [
            { id: 'ups.battery_voltage', unit: 'V', dp: 2, label: '배터리 단자 전압' },
            { id: 'ups.state_of_charge', unit: '%', dp: 1, label: '배터리 충전율 (전류의 적분)' }
        ] },

        { id: 'server', name: '서버 부하', tag: 'server · 랙', kind: 'load',
          x: colX(3), y: ROW2_Y, w: COL_W, h: BOX_H, rows: [
            { id: 'server.load', unit: '%', dp: 1, label: '서버 부하율' }
        ] }
    ];

    // 링크. 전력 채널이 없는 구간이 있는 것은 버그가 아니라 이 장비의 사실입니다. 호스트가
    // 계산하는 전력은 dab.p_in, psfb.p_out, ups.p_batt, ups.p_bus 넷뿐이고, 정류단과 모선에서
    // PSFB 로 들어가는 구간을 재는 채널은 선언된 적이 없습니다.
    //
    // 있는 값을 옆 구간에 겹쳐 그리지 않습니다. 같은 전력이 두 구간을 그대로 흐른다고 말하는
    // 것이 되고, 그것은 손실이 0 이라는, 아무도 측정하지 않은 주장입니다. 같은 이유로 UPS
    // 컨버터의 양쪽에도 채널이 따로 있습니다 — ups.p_bus 는 모선쪽, ups.p_batt 는 배터리
    // 단자쪽이고, 둘의 차이가 그 컨버터의 손실입니다.
    //
    // 값 밑에 항상 채널 id 를 적습니다. 이 그림이 붙인 이름이 아니라 호스트가 선언한 채널
    // 이라는 것을 읽는 사람이 확인할 수 있어야 합니다.
    var LINKS = [
        { from: 'grid', to: 'bus', power: null,
          label: '정류단', noChannel: '이 구간을 재는 전력 채널 없음' },

        { from: 'bus', to: 'dab', power: 'dab.p_in', unit: 'W' },

        // 방향이 아래에서 위입니다 — UPS 갈래가 모선을 떠받치는 쪽이 양수이므로, 정전 중에는
        // 화살표가 모선을 향해 올라갑니다. from 을 아래 상자로 둔 것이 그 부호 규약을 좌표로
        // 옮긴 것이고, 그래서 부호가 뒤집히면 화살표도 뒤집힙니다.
        { from: 'ups', to: 'bus', power: 'ups.p_bus', unit: 'W',
          signLabels: { positive: '모선 지원', negative: '모선에서 충전' } },

        { from: 'bus', to: 'psfb', power: null,
          label: 'PSFB 입력', noChannel: '이 구간을 재는 전력 채널 없음' },

        // 프로파일이 "양수는 뱅크로 들어가는 전력" 이라고 선언하고 있고, 배터리 상자가 아래에
        // 있으므로 양수가 아래쪽 화살표가 됩니다 — 이 파일이 지어낸 뜻이 아니라 선언된 부호
        // 규약을 좌표로 옮긴 것이므로 데이터로 적어 둡니다.
        { from: 'ups', to: 'battery', power: 'ups.p_batt', unit: 'W',
          signLabels: { positive: '충전', negative: '방전' } },

        { from: 'psfb', to: 'server', power: 'psfb.p_out', unit: 'W' }
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
        '.pf-stripe{stroke:none}',
        '.pf-glyph{fill:none;stroke-width:1.4;stroke-linecap:round;stroke-linejoin:round}',
        // 종류별 색. 상자의 왼쪽 띠와 글리프가 같은 색을 쓰므로 한 줄만 바꾸면 둘 다 따라옵니다.
        '.pf-k-source{fill:var(--warn,#FFB020);stroke:var(--warn,#FFB020)}',
        '.pf-k-bus{fill:var(--text-2);stroke:var(--text-2)}',
        '.pf-k-converter{fill:var(--accent,#3D8BFF);stroke:var(--accent,#3D8BFF)}',
        '.pf-k-rail{fill:var(--ok,#2ED47A);stroke:var(--ok,#2ED47A)}',
        '.pf-k-load{fill:var(--text-2);stroke:var(--text-2)}',
        '.pf-k-storage{fill:var(--sim,#C77DFF);stroke:var(--sim,#C77DFF)}',
        '.pf-k-derived{fill:var(--text-3);stroke:var(--text-3)}',
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
        '.pf-over{fill:var(--warn,#FFB020);font-size:8.5px}',
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
        '.pf-pill-bg{fill:var(--panel-2);stroke:var(--line);stroke-width:1}',
        '.pf-pill-t{fill:var(--text-2);font-size:9px;font-weight:600}',
        '.pf-legend{fill:var(--text-3);font-size:11px}',
        '.pf-mono{font-family:"JetBrains Mono",ui-monospace,Consolas,monospace}',
        '.pf-stale{opacity:0.45}',
        '.pf-hide{display:none}'
    ].join('');

    // 넉 줄입니다. 일곱 줄이던 시절 범례가 그림 높이의 40 % 를 차지했고, 설명이 설명하려는
    // 대상보다 커지면 둘 다 읽히지 않습니다. 각 줄은 40 자를 넘습니다 — 검증 하니스가 짧은
    // 문자열만 '화면에 찍힌 값' 으로 보고 검사하기 때문이고, 그래서 여기 적힌 18 kW 같은
    // 눈금 설명이 판독값으로 오인되지 않습니다.
    var LEGEND = [
        '값 없음 = 그 값이 한 번도 도착하지 않았다는 뜻입니다. 회색 점선에 속이 빈 화살표로 그리며, 0 W 와 다릅니다. 0 W 는 실선으로 그리되 움직이지 않습니다.',
        '흐르는 파선의 속도와 굵기는 전력에 비례합니다 — 전 구간 같은 눈금이고 최대치는 18 kW 입니다. 10초 넘게 소식이 없는 값은 흐려지고 경과 시간을 적습니다.',
        '계산값 = 호스트가 여러 채널을 한 시점으로 맞춰 계산한 값입니다. 이 그림은 없는 값을 대신 계산하지 않으며, 컨버터 양쪽 전력도 각자 자기 채널을 씁니다.',
        '세 갈래는 모선에 서로 병렬입니다. UPS 갈래의 화살표가 위를 향하면 배터리가 모선을 떠받치는 중(방전), 아래를 향하면 충전 중입니다. 충전율은 그 전류의 적분입니다.'
    ];

    var LEGEND_TOP = ROW2_Y + BOX_H + 24;
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

    function stageById(id) {
        for (var i = 0; i < STAGES.length; i++) if (STAGES[i].id === id) return STAGES[i];
        return null;
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
    // 종류별 기호. 상자가 전부 똑같이 생기면 어느 것이 전원이고 어느 것이 부하인지 이름을
    // 읽어야만 알 수 있는데, 한눈에 구조를 보라고 그리는 그림에서 그것은 그림이 일을 하지
    // 않고 있다는 뜻입니다. 14 x 14 안에 그리고, 색은 종류 클래스가 정합니다.
    var GLYPHS = {
        source:    'M0,7 q3.5,-5.5 7,0 t7,0',                          // 교류 파형
        bus:       'M0,3.5 h14 M0,7 h14 M0,10.5 h14',                   // 나란한 모선 도체
        converter: 'M1,2 h12 v10 h-12 z M1,12 L13,2',                   // 대각선 넣은 상자
        rail:      'M0,4.5 h14 M0,9.5 h14',                             // 나란한 두 도체
        load:      'M1,2.5 h12 v3 h-12 z M1,8.5 h12 v3 h-12 z',         // 쌓인 랙
        storage:   'M0.5,3.5 h11 v7 h-11 z M11.5,5.5 h2 v3 h-2 z',      // 단자가 있는 전지
        derived:   'M2,12 L12,2 M2.5,3.5 h2 M9.5,10.5 h2'               // 나눗셈 기호
    };

    function glyph(parent, x, y, kind) {
        var d = GLYPHS[kind];
        if (!d) return null;
        return svgEl('path', {
            d: d, transform: 'translate(' + x + ',' + y + ')',
            'class': 'pf-glyph pf-k-' + kind
        }, parent);
    }

    function pill(parent, cx, top, label) {
        var g = svgEl('g', { 'class': 'pf-pill' }, parent);
        svgEl('rect', { x: cx - 19, y: top, width: 38, height: 14, rx: 3, 'class': 'pf-pill-bg' }, g);
        var t = textEl(g, cx, top + 10, 'pf-pill-t', 'middle');
        setText(t, label);
        return g;
    }

    // 값 한 줄. 세로로 쌓는 상자와 옆으로 눕히는 모선이 같은 부품을 쓰므로, 한쪽만 다른 규칙
    // 으로 그려지는 일이 없습니다.
    function buildRow(g, r, x, y, anchor, width) {
        var rg = svgEl('g', { 'class': 'pf-row' }, g);
        var numX = anchor === 'end' ? x + width : x;
        var farX = anchor === 'end' ? x : x + width;
        var farAnchor = anchor === 'end' ? 'start' : 'end';

        var num = textEl(rg, numX, y + 22, 'pf-num', anchor);
        var numT = svgEl('tspan', null, num);
        var unitT = svgEl('tspan', { dx: 4, 'class': 'pf-unit' }, num);
        var status = textEl(rg, farX, y + 22, 'pf-status', farAnchor);
        var idT = textEl(rg, numX, y + 36, 'pf-id pf-mono', anchor);
        setText(idT, r.id);
        // 범위를 넘긴 값에 붙는 주석. 자르지도 숨기지도 않고, 자른 적 없다는 사실을 씁니다.
        var over = textEl(rg, farX, y + 36, 'pf-over', farAnchor);
        setText(svgEl('title', null, rg), r.label);
        return { def: r, g: rg, num: num, numT: numT, unitT: unitT, status: status, over: over };
    }

    function buildStage(svg, def) {
        var bx = def.x, by = def.y, bw = def.w, bh = def.h;
        var g = svgEl('g', null, svg);

        var box = svgEl('rect', { x: bx, y: by, width: bw, height: bh, rx: 12, 'class': 'pf-box' }, g);
        var tint = svgEl('rect', { x: bx, y: by, width: bw, height: bh, rx: 12, 'class': 'pf-tint' }, g);

        // 왼쪽 세로 띠. 상자 안쪽에 그리므로 모서리 반지름을 잘라 낼 필요가 없습니다.
        svgEl('rect', {
            x: bx + 1.5, y: by + 12, width: 3, height: bh - 24, rx: 1.5,
            'class': 'pf-stripe pf-k-' + (def.kind || 'derived')
        }, g);
        glyph(g, bx + PAD, by + 11, def.kind);

        var name = textEl(g, bx + PAD + 21, by + 22, 'pf-name', 'start');
        setText(name, def.name);
        var badge = textEl(g, bx + bw - PAD, by + 22, 'pf-badge', 'end');
        var sub = textEl(g, bx + PAD, by + 38, 'pf-sub pf-mono', 'start');
        setText(sub, def.tag);

        var rows = [];

        if (def.compact) {
            // 모선처럼 낮고 넓은 상자는 값을 오른쪽에 눕혀 답니다. 세로로 쌓으면 들어가지 않고,
            // 높이를 늘리면 모선이 변환기만큼 커져서 한 지점으로 읽히지 않습니다.
            rows.push(buildRow(g, def.rows[0], bx + bw - PAD - 240, by + 12, 'end', 240));
        } else {
            svgEl('line', { x1: bx + PAD, y1: by + 48, x2: bx + bw - PAD, y2: by + 48, 'class': 'pf-div' }, g);
            var top = by + 52;
            var area = bh - 60;
            var y0 = top + (area - def.rows.length * ROW_H) / 2;
            for (var j = 0; j < def.rows.length; j++) {
                rows.push(buildRow(g, def.rows[j], bx + PAD, y0 + j * ROW_H, 'start', bw - PAD * 2));
            }
        }

        return { def: def, box: box, tint: tint, name: name, badge: badge, rows: rows };
    }

    /// 링크 하나의 양 끝점과, 글자를 어디에 놓을지.
    ///
    /// 가로줄과 세로줄이 같은 코드를 씁니다. 방향마다 그리기 함수를 따로 두면 한쪽만 고쳐지는
    /// 날이 오고, 그 날 화면에는 서로 다른 규칙으로 그려진 두 종류의 선이 함께 있게 됩니다.
    function geometry(def) {
        var a = stageById(def.from), b = stageById(def.to);
        var acx = a.x + a.w / 2, bcx = b.x + b.w / 2;
        var acy = a.y + a.h / 2, bcy = b.y + b.h / 2;

        // 위아래로 겹치지 않으면 세로선입니다. 처음에는 중심 사이의 x 거리와 y 거리를 견주어
        // 정했는데, 모선이 814 px 로 넓어지자 바깥쪽 두 갈래는 x 거리가 더 커져서 가로선으로
        // 그려졌습니다 — 상자 크기가 방향을 바꿔 버리는 규칙이었습니다. 겹침 여부는 크기와
        // 무관하게 배치가 뜻하는 그대로입니다.
        //
        // 넓은 상자(모선)와 좁은 상자를 이을 때는 좁은 쪽의 가운데를 씁니다. 모선 한가운데에서
        // 내려오면 세 갈래가 전부 한 점에서 출발해, 병렬이 아니라 하나로 보입니다.
        var overlapY = (a.y < b.y + b.h) && (b.y < a.y + a.h);
        if (!overlapY) {
            var vx = a.w <= b.w ? acx : bcx;
            var down = b.y > a.y;
            var ay = down ? a.y + a.h + 8 : a.y - 8;
            var by = down ? b.y - 8 : b.y + b.h + 8;
            return {
                ax: vx, ay: ay, bx: vx, by: by, vertical: true,
                // 세로선은 글자를 옆에 세웁니다. 선 위아래에 놓으면 상자에 붙습니다.
                lx: vx + 16, ly: (ay + by) / 2, anchor: 'start'
            };
        }

        var right = b.x > a.x;
        var x0 = right ? a.x + a.w + 10 : a.x - 10;
        var x1 = right ? b.x - 10 : b.x + b.w + 10;
        var y = a.h <= b.h ? acy : bcy;
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

        // 18 px 간격입니다. 14 로 두었더니 11 px 짜리 방향 글자와 15 px 짜리 전력 숫자가
        // 브라우저에서 4 px 겹쳤습니다 — DOM 스텁은 글자 크기를 모르므로 잡지 못하고,
        // 실제로 띄워 상자를 재어야만 보입니다.
        var top = G.vertical ? G.ly - 18 : G.ly - 48;

        var mode = textEl(g, G.lx, top, 'pf-mode', G.anchor);
        var power = textEl(g, G.lx, top + 18, 'pf-power', G.anchor);
        var cap = textEl(g, G.lx, top + 31, 'pf-cap pf-mono', G.anchor);

        var L = {
            def: def, G: G, g: g, rail: rail, flow: flow, arrow: arrow,
            mode: mode, power: power, cap: cap, pill: null, speed: 0, offset: 0
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
        setText(svgEl('title', null, svg),
            '고전압 DC 버스에 계통과 세 갈래(DAB, UPS DAB, PSFB)가 병렬로 붙은 전력 흐름');
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
    // a 에서 b 로 향하는 단위 벡터. 가로/세로/어느 방향이든 같은 식으로 다룹니다 — 방향마다
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
                setText(r.over, '');
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
            // 100 % 를 넘겨도 자르지 않고, 숨기지도 않습니다. 시뮬레이터는 채널을 일부러 서로
            // 독립적으로 흔들기 때문에 초과가 정상적으로 나오며, 그것은 데이터의 성질 자체
            // 입니다. 100 으로 눌러 그리면 화면은 그럴듯해지고 그 성질은 사라집니다.
            setText(r.over, (typeof r.def.over === 'number' && e.value > r.def.over)
                ? (r.def.overNote || '') : '');
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
            // 구간에 이름이 있으면 씁니다. 이름이 없으면 그 자리에서 가장 큰 글자가 '값 없음'
            // 이 되어, 측정되지 않은 것은 전력뿐인데 연결 자체가 고장난 것처럼 읽힙니다.
            setText(L.mode, def.label || '');
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

        // 부호에 이름이 붙어 있는 링크에만 씁니다. 정확히 0 은 어느 쪽도 아니므로 구간 이름으로
        // 돌아갑니다 — 0 W 를 '충전' 이라고 쓰면 멈춘 것과 들어가는 것이 화면에서 같아집니다.
        setText(L.mode, def.signLabels && e.value !== 0
            ? (e.value > 0 ? def.signLabels.positive : def.signLabels.negative)
            : (def.label || ''));

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

    function render(state, now) {
        if (!ui) return;
        var i;
        for (i = 0; i < ui.stages.length; i++) renderStage(ui.stages[i], state, now);
        for (i = 0; i < ui.links.length; i++) renderLink(ui.links[i], state, now);
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
