# Device Protocol Analyzer

정사장비를 PC에 연결한 뒤, 장비가 실제로 송신하는 원본 바이트를 수집하고 프로토콜 분석용 세션 파일로 저장하는 Avalonia 데스크톱 진단 도구입니다.

이 프로그램은 장비별 프로토콜을 추측하거나 파싱하지 않습니다. 수신한 `byte[]`를 그대로 보존하고, 별도로 HEX/ASCII 표현을 제공하는 것이 목적입니다.

## 다운로드

[Releases](../../releases)에서 운영체제에 맞는 zip을 받아 압축을 풀고 실행합니다. .NET 설치는 필요 없습니다.

- Windows x64: `DeviceProtocolAnalyzer-win-x64.zip` → `DeviceProtocolAnalyzer.exe`
- macOS Apple Silicon: `DeviceProtocolAnalyzer-osx-arm64.zip` → 서명되지 않은 앱이라 처음에는 우클릭 → 열기로 실행합니다. 막히면 `xattr -cr <폴더>`를 실행하세요.

## 개발 환경

- C# / .NET 10 SDK (`DeviceProtocolAnalyzer.csproj`의 기존 `net10.0` 대상 유지)
- Avalonia 12.1.4
- CommunityToolkit.Mvvm 8.4.2
- System.IO.Ports 10.0.0
- macOS에서 개발·실행, Windows x64에서 배포

## macOS에서 실행

```bash
dotnet restore
dotnet build
dotnet run
```

현재 Mac에 연결된 포트는 `/dev/cu.*`, `/dev/tty.*` 형태로 표시될 수 있습니다. Windows에서는 `COM1`, `COM4`처럼 표시됩니다.

## 사용 방법

1. 정사장비를 PC에 연결합니다.
2. Windows에서는 Device Manager에서 COM Port와 USB-Serial 드라이버를 확인합니다.
3. 프로그램을 실행하고 `새로고침`로 포트 목록을 갱신합니다.
4. Port, Baud Rate, Data Bits, Parity, Stop Bits를 선택합니다.
5. Manufacturer, Model, Test Name, Memo에 테스트 조건을 기록합니다.
6. `세션 시작`을 누릅니다.
7. `연결`를 누르고 장비에서 실제 계수 작업을 수행합니다.
8. RX 로그에서 Timestamp, 수신 바이트 수, HEX, ASCII 표현을 확인합니다.
9. 장비가 Host 명령을 요구하면 Transmit에서 `ASCII` 또는 `HEX`를 선택해 송신합니다.
10. `세션 종료` 후 세션 폴더의 파일을 보관합니다.

`화면 지우기`은 화면에 표시된 RX/TX 목록만 지우며 저장된 파일은 삭제하지 않습니다. `자동 스크롤`을 끄면 실시간 수신 중에도 이전 로그를 확인할 수 있습니다.

## 세션 파일

세션은 실행 파일 옆의 `logs/` 아래에 다음과 같이 저장됩니다.

```text
logs/
└── 2026-10-16/
    └── K6/
        └── 140402_session_001/
            ├── session.raw
            ├── session.hex
            ├── session.log
            └── metadata.json
```

- `session.raw`: 장비에서 수신한 RX bytes만 순서대로 저장합니다. 문자열 변환을 거치지 않습니다. TX 명령은 포함하지 않습니다.
- `session.hex`: RX/TX 각 이벤트를 Timestamp와 2자리 대문자 HEX로 저장합니다.
- `session.log`: CONNECT, DISCONNECT, RX/TX byte count, HEX, ASCII, 오류를 사람이 읽기 쉬운 형태로 저장합니다.
- `metadata.json`: 제조사, 모델, 테스트 이름, 메모, 포트, Baud Rate, Data Bits, Parity, Stop Bits, 시작 시각을 저장합니다.

## Windows 배포

macOS에서 Windows x64용 self-contained 단일 실행 파일을 생성합니다.

```bash
dotnet publish \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true
```

출력 폴더:

```text
bin/Release/net10.0/win-x64/publish/
```

해당 폴더를 Windows PC로 복사한 뒤 `DeviceProtocolAnalyzer.exe`를 실행합니다. 별도 .NET Runtime 설치가 필요하지 않습니다.

## 테스트

```bash
dotnet test tests/DeviceProtocolAnalyzer.Tests/DeviceProtocolAnalyzer.Tests.csproj
```

현재 테스트는 다음 핵심 동작을 검증합니다.

- STX/ETX 등 제어 바이트의 표시 변환과 원본 보존
- HEX 입력 파싱 및 잘못된 입력 거부
- RX raw 파일의 정확한 바이트 보존
- TX 데이터의 HEX/LOG 기록과 raw 파일 비포함
- 세션 metadata 생성

## 현장 테스트 절차

1. 정사장비를 Windows 노트북에 연결
2. Device Manager에서 장비와 COM Port 확인
3. DeviceProtocolAnalyzer 실행
4. 포트 선택 및 통신 설정 입력
5. 테스트 조건과 메모 입력
6. `세션 시작`
7. `연결`
8. 실제 계수 작업 수행
9. RX 데이터, Timestamp, byte count 확인
10. 필요하면 TX 명령을 ASCII/HEX로 송신
11. `세션 종료`
12. `session.raw`, `session.hex`, `session.log`, `metadata.json` 확인 및 백업

## 알려진 제한사항

- 1차 버전은 Serial Port / USB CDC / Virtual COM / RS-232 분석에 집중합니다.
- USB HID, Vendor Specific USB, Ethernet TCP, 파일 출력 장치는 아직 지원하지 않습니다.
- STX/ETX, checksum, 권종, 수량을 자동 해석하지 않습니다.
- 실제 장비가 데이터를 자동 송신하는지, Host 명령에 응답하는지는 현장에서 확인해야 합니다.
- 포트가 표시되더라도 Windows 드라이버, 케이블, 전기적 RS-232 변환기, 장비의 출력 모드에 따라 연결이 실패할 수 있습니다.
- 장비별 Android Protocol Adapter와 Emulator는 수집된 RAW 결과를 분석한 후 별도로 구현해야 합니다.

## 실제 장비에서 확인할 항목

- 물리 포트와 케이블 종류
- Windows 인식 장치명 및 COM Port 번호
- Baud Rate, Data Bits, Parity, Stop Bits
- ASCII/Text인지 Binary인지
- 자동 송신인지 요청-응답 방식인지
- 정사 완료 시점과 프레임 경계
- STX/ETX 및 checksum 존재 여부
- 권종·수량·총액이 포함되는 위치
- 동일 조건을 반복했을 때 변하는 Byte 위치
- 통신 방향에 필요한 TX 명령과 응답 timeout
