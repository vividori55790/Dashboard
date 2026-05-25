# 📦 범용 임베디드 MCU 텔레메트리 모니터링 & 제어 시스템 (Plugin-Driven Workspace)

이 디렉토리는 아두이노, STM32, ESP32 등 다양한 **임베디드 MCU 시스템 전반을 아우르는 고성능 범용 데스크톱 대시보드** 애플리케이션입니다. 

기존의 특정 전력 변환기 레이아웃에 고정된 하드코딩 방식에서 탈피하여, 사용자가 직접 수신 포트, 논리 서브시스템, 변수 매핑 스키마, 패킷 분할 규칙 및 수식 링크를 자유롭게 설계하고, 모든 기능을 독립된 플러그인(Plug-in)으로 동적 탈부착하여 사용할 수 있는 **강력한 확장형 워크스페이스**를 제공합니다.

---

## 📂 프로젝트 파일 구조 (Clean & Modular Structure)

본 프로젝트는 단일 책임 원칙(SRP)에 따라 완벽하게 구조화되어 있어, 코드가 매우 직관적이고 관리가 용이합니다.

```text
Dashboard/
│
├── 🚀 main.py                # [NEW] 중앙 엔트리 런처 (하드웨어 오토디텍트 및 가상 시뮬레이션 지원)
├── 📄 dashboard.py           # 메인 GUI 프레임워크 (Ribbon Bar 및 Dynamic Docking UI)
├── 📄 subsystem.py           # 각 서브시스템 데이터 모델, 링 버퍼 및 한계값 경보 레코드
├── 📄 config_manager.py      # 사용자 워크스페이스 프로필 영구 저장/복원 (config.json)
├── 📄 serial_manager.py      # 다중 동시 USB COM 포트 백그라운드 리드 스레드 관리자
├── 📄 data_router.py         # 패킷 라우터 (PREFIX, JSON, COLUMNS 패킷 매칭 분할 및 가상 수식 연산)
├── 📄 plugin_manager.py      # 플러그인 동적 스캔 및 로딩/언로딩 수명주기 관리자
│
├── 📁 plugins/               # [동적 확장 가능 플러그인 스토어 디렉토리]
│   ├── 📄 base_plugin.py     # 모든 플러그인의 인터페이스 기본 템플릿
│   ├── 📄 telemetry_cards.py # 텍스트 수치 표시 카드 (경보 LED 및 컬러 테마)
│   ├── 📄 trend_charts.py    # 고속 PyQtGraph 카테고리별 스코프 파형 차트
│   ├── 📄 safety_alarms.py   # 안전 알람 한계 한계치 제어 슬라이더 & 깜빡임 경고음
│   ├── 📄 mcu_terminal.py    # 디버그 진단 터미널 및 명령 패킷 송신 콘솔
│   ├── 📄 calibrator.py      # 실시간 선형 보정계수(y = ax + b) 및 라벨 편집기 테이블
│   ├── 📄 matlab_logger.py   # MATLAB/Simulink 연동 호환 고속 CSV 데이터 레코더
│   └── 📄 web_streamer.py    # SSE 기반 로컬 웹 스트리밍 서버 및 HTML 호스트
│
├── 📄 stream_client.html     # 글래스모피즘 테마의 반응형 실시간 모니터링 웹 브라우저 클라이언트
├── 📄 STM32_Example_Code.c   # MCU 측 USB CDC VCP 패킷 전송 예제 소스코드
└── 📄 AGENT.txt              # 프로젝트 개발 표준 규칙 및 변경 이력 추적
```

---

## 🚀 빠른 시작 (Quick Start)

### 1. 필수 라이브러리 설치
Windows PowerShell 또는 명령 프롬프트에서 아래 명령어를 실행하여 구동 환경을 조성합니다:

```bash
pip install PyQt6 pyqtgraph pyserial
```

### 2. 애플리케이션 실행
대시보드 루트 디렉토리에서 `main.py`를 실행하기만 하면 자동으로 모드가 감지되어 시작됩니다:

```bash
python main.py
```

- **실제 하드웨어가 연결된 경우**: 연결된 실제 COM 포트를 인식하여 **실제 하드웨어 연결 모드**로 구동됩니다.
- **연결된 하드웨어가 없는 경우**: `main.py` 런처가 하드웨어 미연결을 자동 감지하고, **가상 시뮬레이션 모드(Virtual Simulation Mode)**로 즉시 자동 전환하여 가상의 STM32(COM3) 및 ESP32(COM4) 노드를 생성해 라이브 데이터 동작을 보여줍니다.

#### 💡 명령줄 강제 구동 옵션
- **시뮬레이션 데모 모드 강제 실행**:
  ```bash
  python main.py --sim
  ```
- **실제 물리 장치 연결 모드 강제 실행**:
  ```bash
  python main.py --hardware
  ```

---

## 💡 핵심 기능 및 워크스페이스 활용 방법

### 1. ⚙️ 서브시스템 & 변수 셋업 위저드 (Setup Wizard)
처음 실행 시 빈 워크스페이스 상태에서 자동으로 프로필 설정 위저드가 열리며, 또는 상단 홈 리본 바의 **`Setup Subsystems & Variables Wizard`**를 눌러 실시간으로 변경할 수 있습니다:
- **🔌 COM Ports**: 연결하여 동시에 패킷을 받을 다중 COM 포트 속도 등록.
- **⚡ Subsystems & Variables**: 임의의 모니터링 노드(예: `EngineNode`, `BatteryNode`, `RobotArm`) 및 고유 변수 정의. 각 변수의 인덱스 슬라이스 위치 설정 및 캘리브레이션 튜닝 값 설정.
- **🚦 Packet Routing Rules**: 각 시리얼 포트에서 수신된 패킷을 나누는 라우팅 타입 설정 (**PREFIX** 프리픽스 매칭, **JSON** 키-값 매칭, **COLUMNS** 순수 열 분할).
- **🔗 Dynamic Data Links**: 서브시스템 경계를 넘는 변수들을 결합해 실시간으로 유기적인 파생 수식을 적용합니다.
  - *예시 (배터리 효율 계산 수식)*: `([BatteryNode].pout / ([BatteryNode].pin + 0.001)) * 100`

### 2. 🧩 플러그인 온/오프 토글 (Plugin Store)
- 상단 리본 바의 **`플러그인 관리`** 탭에서 로딩할 플러그인 기능을 체크박스로 원클릭 활성/비활성할 수 있습니다. 
- UI Docks 구조로 설계되어 있어, 마우스 드래그앤드롭으로 화면의 원하는 위치에 자유롭게 도킹하거나 분리(Float window)하여 다중 모니터에서 커스텀 배치가 가능합니다.
- 새로운 `.py` 파일을 `plugins/` 디렉토리에 추가하기만 하면, 재컴파일 없이 메인 윈도우가 동적으로 발견하여 리본 탭에 활성화해 주는 플러그인 확장성을 지니고 있습니다.

### 3. 🌐 웹 스트리밍 SSE 클라이언트 (`stream_client.html`)
- **Web Stream Server** 플러그인을 활성화하고 `Start Web Stream` 버튼을 누르면, 백그라운드 멀티스레드 웹 서버가 기동됩니다.
- 브라우저를 열고 `http://localhost:8080`에 접속하면, PC뿐만 아니라 로컬 와이파이 망 내의 스마트폰, 태블릿 등 기기에서도 모던 글래스모피즘 테마의 반응형 실시간 차트와 게이지 패널을 동시에 연동해 모니터링할 수 있습니다.

---

## ⚡ MCU 통신 프로토콜 설계 가이드 (VCP Protocol)

### A. Telemetry 데이터 패킷 송신 (MCU -> PC)
대시보드와 정합을 위해 MCU는 설정된 라우팅 룰에 맞춰 데이터를 USB CDC로 출력해야 합니다:
- **PREFIX CSV 방식 예시**:
  - `ENG,[RPM],[TEMP],[VOLTAGE]\n` $\rightarrow$ `ENG,3200.5,72.4,13.8\n`
  - `BAT,[VIN],[IIN],[VOUT],[IOUT],[T_MOS],[T_TRANS],[STBY]\n` $\rightarrow$ `BAT,398.5,8.2,48.0,66.5,42.1,48.4,0\n`
- **JSON 방식 예시**:
  - `{"device":"DAB", "vin":398.5, "iin":8.2, "vout":48.0}\n`

### B. Control Command 제어 명령 수신 (PC -> MCU)
대시보드의 터미널 플러그인을 통해 수동으로 또는 시스템 로직에 의해 송신되는 제어 패킷입니다. MCU 수신 파서 인터럽트 핸들러를 구축하여 사용하세요:
- 기동 명령: `$CMD,RUN\n`
- 차단 명령: `$CMD,STOP\n`
- 초기화 명령: `$CMD,RESET\n`
- 셋포인트 조정: `$CMD,SET_VOUT,[Ref_Float]\n` (예: `$CMD,SET_VOUT,12.50\n`)
