# 🌐 TelemetryDashboard 커스텀 웹 시각화 개발자 가이드

본 시스템은 **"C# 프로그램은 초고속 데이터 수집·이상 분석·보안·로깅 백엔드 프로세스에 집중하고, 화면 시각화는 사용자/개발자가 HTML·JS를 통해 100% 자유롭게 커스터마이징"**할 수 있도록 설계되었습니다.

---

## 🚀 1. 30초 퀵스타트: 나만의 HTML에 데이터 표시하기

### 1단계: HTML 태그 준비
데이터를 표시할 HTML 요소를 만듭니다.
```html
<div class="card">
  <h2>🔥 실시간 온도</h2>
  <span id="val-temp" style="font-size: 32px; font-weight: bold; color: #00FF9D;">0.0</span> °C
</div>
```

### 2단계: SDK 스크립트 추가 & 바인딩 (단 3줄!)
`<body>` 끝에 `telemetry-client.js`를 불러오고 데이터를 연결합니다.
```html
<!-- Telemetry SDK 불러오기 -->
<script src="http://localhost:8080/telemetry-client.js"></script>

<script>
  // 1. 웹소켓 스트림 연결
  TelemetryClient.connect('ws://localhost:8080/ws');

  // 2. 실시간 데이터 수신 시 화면 업데이트
  TelemetryClient.onData((data) => {
    if (data.temp !== undefined) {
      document.getElementById('val-temp').textContent = data.temp.toFixed(1);
    }
  });
</script>
```

---

## 📦 2. 기본 제공되는 5가지 스타터 템플릿

프로젝트 폴더 내에 즉시 실행하고 수정할 수 있는 5가지 기본 HTML 템플릿이 포함되어 있습니다:

| 템플릿 파일 | 설명 | 권장 용도 |
|---|---|---|
| **[`starter_minimal.html`](file:///c:/Users/vivid/Documents/GitProject/Dashboard/starter_minimal.html)** | **30줄 초간단 실시간 수치 카드**<br>온도, 진동, 습도, RPM, 이상치(Z-Score) 표시 및 이상 발생 시 카드 붉은색 점멸 | 초보자 빠른 시작, 기본 센서 모니터링 |
| **[`starter_chart_gauge.html`](file:///c:/Users/vivid/Documents/GitProject/Dashboard/starter_chart_gauge.html)** | **Chart.js 실시간 롤링 그래프 & SVG 게이지**<br>20Hz 고속 시계열 스크롤 꺾은선 차트 및 모터 RPM 원형 게이지 | 파형 분석, 모터/배터리 게이지 뷰 |
| **[`starter_grid_dashboard.html`](file:///c:/Users/vivid/Documents/GitProject/Dashboard/starter_grid_dashboard.html)** | **다채널 센서/노드 반응형 그리드**<br>수신되는 모든 MCU/센서 노드 카드가 자동 생성되고 이상 징후 노드 점멸 | 다중 장비/공장 관제, 모바일/태블릿 뷰 |
| **[`stream_client.html`](file:///c:/Users/vivid/Documents/GitProject/Dashboard/stream_client.html)** | **2D 노드 와이어 & 0.1초 타임트래블 DVR 콘솔**<br>노드 간 데이터 흐름 시각화, 과거 시간대 재생 및 AI 사고 리포트 생성 | 엔터프라이즈 통합 콘솔 |
| **[`custom_dashboard.html`](file:///c:/Users/vivid/Documents/GitProject/Dashboard/custom_dashboard.html)** | **노코드(No-Code) 드래그앤드롭 대시보드 빌더**<br>코딩 없이 원하는 위젯을 마우스로 배치하고 즉시 내보내기 | 빠른 레이아웃 프로토타이핑 |

---

## 📡 3. 실시간 JSON 패킷 스키마 규격

WebSocket(`ws://localhost:8080/ws`)을 통해 매 초 20~1,000회 전송되는 JSON 패킷의 필드 규격입니다:

```json
{
  "nodeId": "DAB_CONVERTER",      // 센서/MCU 고유 식별자 (예: MCU_NODE_1, DAB_CONVERTER)
  "temp": 42.85,                  // 온도 값 (단위: °C)
  "vibration": 0.312,             // 진동 값 (단위: g)
  "humidity": 55.4,               // 습도 값 (단위: %)
  "rpm": 3200,                    // 모터 속도 (단위: RPM)
  "anomalyScore": 0.42,           // ML 실시간 Z-Score 이상치 (2.5σ 이상 시 이상 징후)
  "isAnomaly": false,             // 이상 상태 여부 (true / false)
  "timestamp": "2026-08-14T19:00:00.123Z" // ISO-8601 타임스탬프
}
```

---

## 💡 4. 자주 쓰이는 고급 기능 활용법

### ① 특정 센서 노드만 선별 수신
여러 대의 컴퓨터/MCU가 연결되어 있을 때 특정 노드만 골라서 처리할 수 있습니다:
```javascript
TelemetryClient.onChannel('MCU_NODE_2', (data) => {
    console.log('MCU 2번 데이터 수신:', data.temp);
});
```

### ② 이상 징후(Anomaly) 발생 시 알림 팝업
```javascript
TelemetryClient.onAnomaly((anomalyPacket) => {
    alert(`🚨 [긴급 경보] ${anomalyPacket.nodeId} 이상 발생! Z-Score: ${anomalyPacket.anomalyScore}σ`);
});
```

### ③ AI 사고 분석 리포트(Markdown) 가져오기
```javascript
const report = await TelemetryClient.getIncidentReport();
console.log(report.markdown); // 마크다운 형식의 AI 분석 보고서
```

---

## 🛠️ 5. React / Vue / 모바일 프레임워크 연동

### React (Hooks) 예제
```jsx
import { useEffect, useState } from 'react';

export function SensorWidget() {
  const [temp, setTemp] = useState(0);

  useEffect(() => {
    const ws = new WebSocket('ws://localhost:8080/ws');
    ws.onmessage = (e) => {
      const data = JSON.parse(e.data);
      if (data.temp !== undefined) setTemp(data.temp);
    };
    return () => ws.close();
  }, []);

  return <div>온도: {temp.toFixed(1)}°C</div>;
}
```

---
*가이드 버전: 2.0 (TelemetryDashboard Universal Web Integration)*
