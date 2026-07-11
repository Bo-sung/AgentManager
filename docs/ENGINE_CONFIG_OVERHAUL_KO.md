# 설정파일 재정비 + 커스텀 엔진 (Engine Config Overhaul) — SSOT

## 목표
설정을 **성격별로 3분할**하고, 엔진별 설정을 **엔진 1개 = 파일 1개**로 통합해 **커스텀 엔진 추가**의 토대를 만든다. (기존 `docs/HANDOFF_CUSTOM_ENGINE_PLATFORM_KO.md` Phase B/C/E를 "설정 재정비" 관점으로 재구성.)

- **공통(전역)** → `settings.json` (정리 후: Ollama·번역·MaxConcurrent*·Skill·Theme/Accent/Scale·Language·ApprovalPolicy·WorktreeBase·AutoStart·StreamLogs·Telemetry·ReviewPaneOpen·WarnNoWorktree)
- **엔진별** → `engines/<id>.json` (경로·인증·기본모델·선호모델·skillDir·모델+effort·enabled·adapterKind·identity·launch·capabilities·roles). `models.json`(v1.21.0)을 흡수.
- **런타임 상태** → `state.json` (Usage·EngineLimitedUntil·DismissedCliSessions)

## 파일 구조
```
%LOCALAPPDATA%\AgentManager\
  settings.json          # 공통만
  state.json             # 런타임 상태 포함
  engines\<id>.json      # cc/gx/agy/pi(+ pi-worker) 내장 + 커스텀
```

## 스키마 (Core `AgentManager.Core.Engines`)
- `EngineConfig`(record): id·name·badge·source(builtin|custom)·adapterKind·cli·desc·installUrl·enabled·path·launch·auth·skillDir·defaultModel·defaultEfforts·models[]·allowedRoles. 헬퍼: ModelIds/PreferredModelIds/EffortsFor/DefaultEffortFor/HasEfforts.
- `EngineModelConfig`(id·efforts?·defaultEffort?·preferred), `EngineAuthConfig`(mode·apiKeyEnc·autoApiOnLimit), `EngineLaunchConfig`(exe·args).
- `DefaultEngineConfig.Build()` — `EngineRegistry.All` + `DefaultModelCatalog`로 내장 엔진 시드(현행 동작 재현). `AdapterKindFor(id)`: cc=claude-stream-json·gx=codex-json·agy=agy-pty·pi=pi-rpc.
- `EngineConfigStore` — `engines/*.json` load/seed/save(atomic)/Upsert/Remove(custom만). 내장 파일 손상/부재 시 재시드, 잘못된 커스텀 파일은 skip(무예외).

## 단계
- **P1 (진행 중)** settings.json 정리 + `EngineConfigStore` + 마이그레이션.
  - **P1a ✅ (this branch)**: Core `EngineConfig`/`EngineConfigStore`/`DefaultEngineConfig` + 스모크(`engine config asserts OK`). 아직 VM 미배선(신규 코드만).
  - **P1b**: 마이그레이션 — 기존 settings.json 엔진별 키(경로 필드·DefaultModels·PreferredModels·SkillDirs·EngineAuth*·DisabledEngines) + `models.json` → `engines/*.json` 1회 이관(무손실, 최소 1릴리즈 읽기 호환).
  - **P1c**: VM 배선 — 엔진별 값 read/write를 EngineConfigStore로 라우팅(ClaudePath/EngineAuthMode/DefaultModels/PreferredModels/models.json 대체).
  - **P1d**: settings.json 슬림화 — 엔진별·런타임 키를 DTO에서 제거, 런타임 상태는 state로.
- **P2** 분리된 모델/엔진 설정 하위 페이지(설정 진입) — engines 파일 기반 모델 add/remove·effort·기본·선호·경로·인증 편집(=Q3-A 매니저, "여러 개 추가").
- **P3** 데이터 구동 엔진 등록 + `AdapterKind`/`AdapterFactory`(구 Phase B). `AgentId` 광범위 박힘 → 점진.
- **P4** 커스텀 엔진 추가 — 매니페스트(=engines/*.json) + one-shot-text/bridge-jsonl 어댑터 + trust/보안(exe·args 승인, ArgumentList only, secret 마스킹)(구 Phase C/D/E).

## 리스크/주의
- `AgentId`(엔진 id)가 UI·persistence·워커 생성 전반에 박힘 → 회귀 위험, 점진 진행.
- XAML 중복 x:Key = 시작 크래시(로컬라이즈 문자열 parity 확인).
- one-shot 어댑터는 현 라인별 JSONL 세션 루프에 exit-완료 훅 필요(P4).
- 하위호환: 기존 사용자 설정 무손실 이관(P1b) 전에는 배선(P1c)·슬림화(P1d) 금지.

## Do Not
- 개발 PC 절대경로 런타임 사용 금지. shell string 실행 금지(ArgumentList). push/merge/release 사용자 승인 없이 금지.
