# 릴리즈 코드 서명 (Authenticode)

`scripts/release.ps1`로 만든 설치본(`Releases\AgentManager-win-Setup.exe`, `Update.exe`, 그리고
Velopack이 패키징하는 ~400개 파일)에 **Authenticode 코드 서명**을 붙이는 방법을 정리한다.

서명 배선(플러밍)은 이미 스크립트에 들어가 있다 — **인증서만 있으면** 바로 서명 릴리즈를 낼 수 있다.
인증서·비밀번호는 **절대 커밋하지 않고** 환경 변수로만 공급한다(`*.pfx`는 `.gitignore` 처리됨).

---

## 서명이 하는 일 / 하지 않는 일 (정확히)

**하는 일 (OS 신뢰 계층):**
- `Setup.exe`/`Update.exe` 실행 시 뜨는 "알 수 없는 게시자(Unknown Publisher)" 빨간 UAC 경고 제거 → 조직명 표시.
- SmartScreen 평판(reputation) 축적 → 첫 실행 차단이 점차 사라짐(평판은 **실제 서명본이 배포된 뒤부터** 쌓인다).
- 서명되지 않은 부트스트래퍼에 대한 백신/Defender 오탐(false positive) 감소.
- 다운로드된 업데이트가 변조되면 OS 수준에서 감지 가능(tamper-evident).

**하지 않는 일 (앱 내부 암호 검증 아님):**
- Velopack 1.2.0은 다운로드한 `.nupkg`를 **RELEASES 피드의 SHA 체크섬**으로만 검증한다.
  다운로드 패키지의 Authenticode 인증서를 **검증하지 않는다**.
- 즉 여기서 "서명 업데이트"는 **OS 신뢰 + 평판**을 의미하며,
  `AppViewModel.Update.cs`의 다운로드/적용 로직에 새로운 암호 검증 게이트를 추가하는 것이 **아니다**.

---

## 어떤 인증서가 필요한가

| 종류 | 저장 형태 | SmartScreen | 비고 |
|------|-----------|-------------|------|
| **OV** (조직 검증) | 파일 `.pfx` (2023년 정책 변경 후 실제로는 하드웨어 토큰/HSM 권장) | 평판을 **서서히** 축적 | 저렴, 개인/소규모 |
| **EV** (확장 검증) | FIPS-140 하드웨어 토큰 / 클라우드 HSM (파일 내보내기 불가) | **즉시** 평판 | 파일 `.pfx` 불가 → **thumbprint 경로만** |

발급처: DigiCert, Sectigo, SSL.com 등.

> 2023년 6월 이후 CA/B 포럼 정책상 OV 코드서명 키도 하드웨어(HSM/토큰) 보관이 요구되어,
> 순수 파일 `.pfx` 경로는 레거시/사내 CA/테스트 시나리오로 좁혀졌다. 신규 발급이라면
> **thumbprint(인증서 저장소) 경로**를 기본으로 보는 편이 안전하다.

---

## 인증서 설치 & thumbprint 확인

1. `.pfx`를 현재 사용자 저장소로 가져오기(또는 토큰/HSM 드라이버 설치):
   - 탐색기에서 `.pfx` 더블클릭 → **현재 사용자(Current User)** → `개인(Personal / My)` 저장소로 가져오기.
2. thumbprint(지문, SHA1) 읽기:
   ```powershell
   Get-ChildItem Cert:\CurrentUser\My | Format-List Subject, Thumbprint, NotAfter
   ```
   출력된 `Thumbprint`(공백 없는 40자리 hex)를 사용한다.

---

## 서명 릴리즈 실행

두 가지 모드 중 하나로 환경 변수를 세션에 설정한 뒤 평소처럼 `release.ps1`을 돌린다.
**명시적 `-Sign`이 최우선**, 그 다음 thumbprint, 그 다음 pfx 순으로 적용된다.

### (권장) 인증서 저장소 thumbprint
```powershell
$env:AM_SIGN_THUMBPRINT = "A1B2C3...D4E5F6"      # 위에서 확인한 40자리 hex
scripts\release.ps1 1.21.5 -Upload
```
- EV 토큰/HSM에 있는 인증서도 이 경로로 동작한다(파일·비밀번호가 디스크에 남지 않음).

### (대안) 파일 .pfx
```powershell
$env:AM_SIGN_PFX = "C:\certs\am.pfx"
$env:AM_SIGN_PFX_PASSWORD = "..."               # 세션 한정, 머신 영구 설정 금지
scripts\release.ps1 1.21.5 -Upload
```
- 스크립트는 pfx 파일이 없거나 비밀번호가 비어 있으면 **패키징 전에 즉시 중단(fail-fast)** 한다.

### (명시적 override)
```powershell
scripts\release.ps1 1.21.5 -Sign "/sha1 A1B2... /fd sha256 /tr http://timestamp.digicert.com /td sha256"
```

> 스크립트는 어떤 경로든 항상 `/fd sha256`(SHA-256 다이제스트)과
> `/tr <TSA> /td sha256`(RFC-3161 타임스탬프)를 붙인다. **타임스탬프가 없으면
> 인증서 만료 즉시 이미 배포된 서명이 전부 무효**가 되므로 절대 제거하지 말 것.
> 기본 TSA는 `http://timestamp.digicert.com`이며 `-Timestamp`로 바꿀 수 있다.

---

## 보안 주의사항

- **`.pfx`·비밀번호를 커밋하지 않는다** — `*.pfx`는 이미 `.gitignore` 처리됨(로컬 헬퍼는 `signing.local.ps1` 등도 무시).
- **thumbprint 경로를 우선**하라 — pfx 경로는 `signtool` 명령줄에 `/p <비밀번호>`가 노출되어
  실행 중 같은 머신의 다른 프로세스/사용자에게 보일 수 있다.
- 환경 변수는 **현재 셸 세션에만** 설정하고, 시스템/머신 영구 변수로 저장하지 않는다.

---

## 검증

서명 패키징 후:
```powershell
signtool verify /pa /v Releases\AgentManager-win-Setup.exe
```
- 게시자(Subject)와 타임스탬프가 표시되면 성공.
- 또는 `Setup.exe` 우클릭 → **속성 → 디지털 서명** 탭에서 서명자 확인.

> `signtool.exe`는 **Windows SDK**에 포함되어 있고 `PATH`에 있어야 한다(vpk가 내부적으로 호출).
> 없으면 pack 단계에서 서명이 실패하므로 Windows SDK를 설치하거나 signtool 경로를 PATH에 추가한다.

---

## 클라우드/키리스 서명 (선택)

Azure Trusted Signing, DigiCert KeyLocker(`smctl`), `AzureSignTool` 등 명령 기반 서명을 쓰려면
`--signParams`(signtool 인자) 대신 vpk의 `--signTemplate "<명령> ... {{file}}"`를 사용한다.
이 경우 `release.ps1`에 작은 분기 하나(예: `$env:AM_SIGN_TEMPLATE`를 읽어 `--signTemplate`로 전달)를
추가하면 된다 — 현재는 배선돼 있지 않으므로 그 경로를 택할 때 추가한다.

---

## 성능/운영 메모

- vpk는 `Setup.exe`/`Update.exe` + 패키징된 DLL(~400개)에 모두 서명하므로 **느리고 TSA 네트워크에 의존**한다.
  TSA가 불안정하면 pack이 실패할 수 있다. 설치본만 빠르게 서명하려면 `--signSkipDll`을
  고려할 수 있으나, 이 경우 앱 폴더 내부 DLL은 서명되지 않는 트레이드오프가 있다.
- **EV 하드웨어 토큰**은 파일마다 PIN 입력을 대화식으로 요구할 수 있어, 무인 `-Upload` 실행이
  멈출 수 있다(스크립트로 우회 불가). 서명 릴리즈가 완전 무인이 아닐 수 있음을 감안한다.

---

## 현재 상태

- **배선/문서: 완료.** `release.ps1`은 인증서만 있으면 서명 릴리즈를 낼 수 있다.
- **차단(BLOCKED): 실제 서명 실행** — 사용자가 Authenticode 인증서(OV 파일 vs EV 토큰 결정 포함)를
  제공해야 `signtool verify` 증빙과 SmartScreen 평판 축적이 시작된다.
  이 결정은 어느 서명 분기(thumbprint/pfx)가 실행되는지만 바꾸며, 코드 자체는 바꾸지 않는다.
