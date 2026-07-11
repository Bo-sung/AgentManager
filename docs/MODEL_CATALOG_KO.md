# 모델 카탈로그 파일 (`models.json`)

엔진별 **지원 모델 목록**과 **모델별 추론(effort) 단계**를 담는 사용자 편집 파일. 기존에 코드에 하드코딩돼 있던 목록을 대체하여, 새 모델·추론 단계를 **코드 수정 없이 파일 편집으로 추가**할 수 있다.

## 위치
```
%LOCALAPPDATA%\AgentManager\models.json
```
- 최초 실행 시 현재 기본값으로 자동 생성(seed). 파싱 불가/손상 시 기본값으로 재생성(견고).
- **선택(preferred)한 모델**은 여기 두지 않는다 — 그건 `settings.json`의 `PreferredModels` 그대로. 이 파일은 "사용 가능한 카탈로그"만 담는다.

## 스키마
```json
{
  "schemaVersion": 1,
  "engines": {
    "cc": {
      "defaultEfforts": ["default","low","medium","high","xhigh","max"],
      "models": [
        { "id": "opus", "efforts": ["default","low","medium","high","xhigh","max","ultracode"], "defaultEffort": "medium" },
        "haiku"
      ]
    },
    "gx": { "defaultEfforts": ["low","medium","high","xhigh"], "models": [
        { "id": "gpt-5.6-luna", "efforts": ["low","medium","high","xhigh","max"], "defaultEffort": "medium" },
        { "id": "gpt-5.5", "efforts": ["low","medium","high","xhigh"], "defaultEffort": "xhigh" }
    ]},
    "agy": { "defaultEfforts": [], "models": ["default","Gemini 3.5 Flash (High)"] },
    "pi":  { "defaultEfforts": ["default","off","minimal","low","medium","high","xhigh"], "models": ["default","zai/glm-4.7"] }
  }
}
```

### 규칙
- **models 항목**: 문자열(엔진 `defaultEfforts` 상속) 또는 객체 `{ id, efforts?, defaultEffort? }`.
- **efforts(모델별)가 1급** — 추론 단계는 모델마다 다르다(gx `gpt-5.6-luna`는 `max` 있음/`gpt-5.5`는 기본 `xhigh`; cc `ultracode`는 xhigh 모델만; pi `thinking` 미지원 모델 등). 모델에 `efforts`가 있으면 그걸 사용, 없으면 엔진 `defaultEfforts`.
- **defaultEffort(모델별)**: 그 모델의 기본 추론 단계(피커의 "(default)"). 없으면 스마트 기본(cc opus=medium 등).
- **defaultEfforts 빈 배열** = 추론 차원 없음(agy — effort가 모델 라벨에 내장). effort 피커 숨김.
- effort 토큰은 어댑터가 전달하는 정식값: cc `--effort`, gx `model_reasoning_effort`, pi `--thinking`. ("Extra high"=`xhigh`, "Max"=`max`).

## 동작
- **필터/드롭다운/피커의 모델 목록·추론 옵션 = 이 파일**. 파일을 직접 편집하면 즉시(재시작 시) 반영된다 → 직접 추가가 안 뜨던 문제 해결.
- **pi/agy 모델 조회**(`pi --list-models` / `agy models`) 후, 결과가 파일과 다르면 해당 엔진 `models`를 갱신(기존 모델의 per-model efforts는 보존, 신규 모델은 엔진 기본 상속). 조회 결과가 비면 덮어쓰지 않음.
- **cc/gx는 모델 목록 CLI가 없음** → 조회 없음 → 파일 편집이 유일한 갱신 경로(그래서 파일화가 특히 유용).

## 코드
- Core: `AgentManager.Core.Models.ModelCatalog`(load/seed/save/`ModelsFor`/`EffortsFor`/`DefaultEffortFor`/`HasEfforts`/`UpdateFromQuery`) + `DefaultModelCatalog`(seed).
- VM: `AppViewModel._modelCatalog` — `EngineModels`·`SessionViewModel.EffortOptions/HasEffort/RecommendedEffort`·New-Agent 피커가 카탈로그에서 읽음. 조회 메서드가 `UpdateFromQuery` 호출.
- 진단: `dotnet run --project src/AgentManager.Smoke -- --dump-model-catalog` 로 기본 카탈로그 출력.
