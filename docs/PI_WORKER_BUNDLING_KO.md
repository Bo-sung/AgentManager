# Pi Worker 런타임 번들링 (ADR)

## 상태
채택(2026-07-11). v1.20.0에서 Pi Worker는 글로벌 npm 설치나 로컬 경로 지정이 필요했음 → 설치본에 **번들**하여 일반 사용자가 추가 설치 없이 사용하도록 함.

## 결정
`@agentmanager/pi-worker-harness`(commit `6e49dbd`, MIT, 런타임 npm deps 0)의 **런타임 파일만** AgentManager 저장소에 **vendor**한다.

- 위치: `engines/pi-worker/` — `dist/`(빌드 JS) + `worker-package/`(extensions/skills/prompts) + `package.json` + `NOTICE.md`(출처/커밋/라이선스).
- vendor 제외: `src/`, `tests/`, `node_modules/`, 개발 tooling (런타임에 불필요; harness는 순수 node 내장 모듈만 사용).
- `.gitignore`의 전역 `dist/` 규칙을 `engines/pi-worker/dist/`에 대해 negation으로 예외 처리(vendor된 dist는 추적).
- publish 번들: `AgentManager.csproj`의 `<None Include="..\..\engines\pi-worker\**\*" CopyToOutputDirectory Link="runtimes\pi-worker\..." />` → 출력 `<app>/runtimes/pi-worker/`. framework-dependent single-file · self-contained(Velopack) 모두에 포함(실측 확인).

## 대안 비교(§3.2)
1. **vendor/subtree(채택)** — CI/clone/release 재현성 최상, 커밋 고정, 오프라인. harness가 tiny(~38K)·런타임 deps 0이라 부담 없음. 단점: 업스트림 변경 시 재-vendor 필요(NOTICE의 source commit 갱신).
2. git submodule(고정 커밋) — 재현성 좋으나 clone/CI에 `submodule update` 필요, publish 파이프라인 복잡. 기각.
3. versioned archive 포함 — vendor와 유사하나 diff 가독성↓. 기각.
- **로컬 PC 경로에서 publish 시 복사(금지)**: 개발 머신 의존 → 재현성 없음. 절대 사용 안 함.

## 런타임 탐지 우선순위 (`EngineRegistry`)
1. 사용자 override(`PiWorkerPath`; Phase B에서 engine runtime override로 migration)
2. **번들** `AppContext.BaseDirectory/runtimes/pi-worker/dist/cli/index.js` (`BundledPiWorker()`)
3. 글로벌 npm 설치(레거시 fallback)
4. 미발견 → `EngineUnavailable`
- 일반 사용자는 (2)로 글로벌 설치 없이 동작. 공식 Pi(child)와 Node는 여전히 필요 — 별도 진단(harness `doctor`).

## 실행
- 번들 entrypoint는 순수 node로 구동: `node <app>/runtimes/pi-worker/dist/cli/index.js --mode rpc ...`. `PiAdapter`가 `.js`→node 분기(기존과 동일). worker-guard/worker-task는 `worker-package`에서 로드, 세션은 `~/.pi-worker`.

## 검증
- `dotnet build` 후 `bin/.../runtimes/pi-worker/` 존재 + `--version` = `pi-worker 0.1.0 (wraps official pi 0.80.3)`.
- framework-dependent single-file publish 출력에 `runtimes/pi-worker/`(11 files) 포함.
- 스모크 14 groups green(회귀 없음).

## 업스트림 갱신 절차
harness 결함 수정 시 upstream(`H:\Git\Bosung_PI\pi-worker-harness`)에서 고치고 `dist/`+`worker-package/`+`package.json`을 재복사, `NOTICE.md`의 source commit 갱신. 이 vendor 디렉토리는 직접 수정하지 않는다.
