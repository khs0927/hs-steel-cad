# HS-Steel CAD 아키텍처 v0.3 — 도면 생성 전용

작성: 2026-10-02 · 이전판: `C:\HS-STEEL_REBORN\ARCHITECTURE.md`(v0.2)

## 0. 확정된 결정

| 항목 | 결정 |
|---|---|
| 범위 | **도면 생성만**: 단품 상세도, 조립도, 배치/자동작도, 물량(도면 내 표·XData). **출력(Plot/PDF)은 범위 밖**(사용자 담당) |
| 도곽 | **회사 도곽 블록에 넣는다.** 도곽은 입력(`SheetFrame`: 블록명, 크기, 작도 가능 영역). 회사별 프로파일 없음 |
| 입력 방식 | **모델 우선(Model-first)** — 아래 §1 |
| 저장소 | 별도 저장소 `C:\CODE\hs-steel-cad` 에서 개발 → 이후 power-cad-mcp에 병합 |
| 언어 | C# .NET 10 (power-cad와 동일 빌드 설정) + **C++ ObjectARX 모듈(HsSteel.Arx) 포함** |

## 1. 입력 방식: 왜 모델 우선인가

상세도를 "잘" 만들려면 도면은 결과물이고, 원천은 **구조 모델**(그리드·부재 축선·접합부)이어야 한다.
Tekla·Advance Steel이 같은 방식이며, 이렇게 해야 다음이 자동으로 일관된다.
- 부재 길이·절단·구멍은 **접합부 규칙**(볼트 개수/피치/게이지, 스캘럽, 엔드플레이트)에서 계산 → 사람이 구멍 위치를 입력하지 않음
- 같은 부재는 같은 마크로 묶여 상세도 1장, 수량은 자동
- 상세도·조립도·배치도·물량이 모두 같은 모델에서 나와 서로 어긋나지 않음

모델로 들어오는 경로(모두 같은 `Project` 모델로 수렴):
1. **대화형(MCP)** — "X1~X5 그리드, 2층 보 H400 연결은 전단 3볼트" 같은 구조화 입력
2. **일괄 가져오기** — 엑셀, 원본 HS-STEEL `.Mxx`/`.dat` 공사 데이터
3. **골조 평면 인식** — 기존 DWG의 단선(레이어·문자 기반) → 부재 축선 + 규격

## 2. 계층 구조

```
HsSteel.Assets      원본 자산 읽기(CP949 .dat, Project.dat 규칙, 블록 DWG)      ← CAD 의존 0
HsSteel.Domain      단면·부재·볼트그룹·접합부·Project, 상세 규칙(DetailRules)
HsSteel.Modeling    [진행] 그리드·축선·접합부 규칙 → 부재 형상(길이·구멍·스캘럽/cope·엔드플레이트) 확정, 마크 부여
HsSteel.Drafting    Project → DrawPlan (power-cad cad_create JSON과 동일 스키마)
   ├ MemberDetailGenerator  (구현됨: H형강 단품 — 입면·평면·단면·구멍·스캘럽·끊김표시·실치수)
   ├ AssemblyDetail / Layout / BomTable / Marking  [다음]
   ├ SheetFrame (회사 도곽 영역에 배치·축척 결정)
   └ DxfExporter (ACadSharp, AutoCAD 없이 DXF)
HsSteel.Mcp         MCP 툴 (hs_section_search, hs_draw_member_detail)  ← 병합 시 power-cad-server로 이동
native/HsSteel.Arx  C++ ObjectARX 커스텀 엔티티 HsMember (P5)
```
규칙: Drafting 이하는 AutoCAD.NET을 참조하지 않는다. 같은 DrawPlan을 DXF(지금), power-cad의 AutoCAD 플러그인(병합 후), 시뮬레이터에 그대로 적용한다.

## 3. power-cad 병합 계획

| 지금(별도 저장소) | 병합 후 |
|---|---|
| `DrawPlan` 내부 spec = `cad_create` JSON, HS tag는 별도 | `hs_draw_plan_handoff`의 `spec`만 Power CAD create plan으로 전달하고 `tag`는 XData 증거로 별도 보존 |
| `HsSteel.Mcp` 독립 서버 | 툴 클래스를 `PowerCad.Server`에 등록 (같은 MCP SDK 2.2) |
| `DxfExporter` (ACadSharp) | `PowerCad.Dxf` 백엔드로 승격 |
| — | power-cad에 추가 필요: XData 읽기/쓰기, 대량 생성(`create_many`, 현재 20단계 한도), 블록 속성 읽기, TABLE 생성 |

빌드 설정(`Directory.Build.props`: net10.0, Nullable, TreatWarningsAsErrors)은 power-cad와 동일하게 맞춰 두었다.

## 4. 설계 원칙

1. **HS-STEEL 자산 무누락** — `C:\HS-STEEL` 1,801개 파일 전부를 [자산 대장](ASSET_LEDGER.md)에 등재하고, 파일마다
   ingest / reference / excluded(사유) 처분을 둔다. 대장에 없는 파일이 있으면 CI 실패.
2. **클린룸** — 원본 VLX/DLL은 이식하지 않는다. 데이터와 관찰된 동작만 명세로 쓴다. 동글 관련 파일은 제외.
3. **모델이 원천, 도면은 결과** — 도면에 손으로 넣는 값을 최소화. 모든 부재 형상은 모델·접합 규칙에서 계산.
4. **실치수 원칙** — 끊김 표시로 줄여 그려도 치수는 항상 실제 값. 테스트로 검증.
5. **도곽 안에 들어간다** — 모든 뷰·치수·주기는 도곽의 작도 영역 안. 테스트로 검증.
6. **결정론** — 같은 입력 → 바이트 동일 DrawPlan.
7. **독립 검증** — 표 단중과 이론 단중을 대조하는 식으로, 정답지는 원본 산출물이나 독립 계산이어야 한다(자기 대조 금지).
8. **도면이 데이터를 품는다** — 병합 후 모든 부재에 XData(마크·규격·길이·조립). 물량은 도면에서 역산 가능.

## 5. 현재 상태 (실측)

- `dotnet test`: 8/8 통과 — 원본 규격표 25종 전부 파싱, Project.dat 규칙 읽기, 끊김 매핑, 구멍 12개·실치수 체인, 결정론, 도곽 영역 준수, DXF 저장 후 재읽기
- `out/demo_B1.dxf/.png`: H400x200x8x13 L=9000, 양단 웹 3x2 볼트, 스캘럽 → 1/15, 끊김 표시 포함
- 확인된 품질 과제(다음 단계): 구멍 체인 치수가 좁은 간격에서 겹침 → 누적치수/인출 처리, 단면도 확대 축척, 구멍 크기·볼트 주기(예: 6-M20 HTB), 용접 기호, 도면 하단 영역 활용
- `native/HsSteel.Arx`: 헤더·CMake만. 빌드 전제(VS2022 C++ 빌드도구, ObjectARX 2027 SDK)가 이 PC에 없음

## 6. 로드맵

| 단계 | 내용 |
|---|---|
| D1 | 단품 상세도 품질: 치수 배치 엔진(겹침 회피), 볼트·용접 주기, 단면 확대, 회사 도곽 실측 적용 |
| D2 | 형강 확장: ㄱ형강·채널·각관·파이프·판재 상세도 (자산의 25개 규격표 전부) |
| D3 | Modeling: 그리드 프레임 + EndPlate/ShearTab + flange cope/scallop + 마크 통합 (**부분 완료** 2026-10-08); 상세도 cope 작도·대화형 그리드 규칙 MCP는 잔여 |
| D4 | 조립도(부재+판+볼트), 배치/입면 자동작도, 도면 내 BOM/볼트표 |
| D5 | 원본 블록 114개 라이브러리화(ACadSharp 읽기), 원본 `.Mxx` 가져오기 |
| M | power-cad 병합 (XData, create_many 추가 후) |
| P5 | C++ HsMember 커스텀 엔티티 |


### Power CAD handoff safety

`hs_draw_plan`은 기존 호환용이며 각 엔티티에 `hs` 태그가 포함되므로 Power CAD
`cad_create`에 직접 전달하지 않는다. 신규 `hs_draw_plan_handoff`는
`hs-steel-draw-plan/1`을 반환하며 `spec`과 `tag`를 분리한다.

handoff는 canonical JSON SHA-256 `contract_digest`를 포함하고
`execution_authorized=false`, `may_execute_mutation=false`이다. Power CAD는 이
계약을 검증하고 create steps로 준비할 뿐이며, 실제 수정은 별도의 live document
binding, snapshot, plan preview, 승인, commit 절차를 거친다. 현재 XData 쓰기는
아직 구현되지 않았으므로 tag를 조용히 버리지 않고 pending evidence로 유지한다.
