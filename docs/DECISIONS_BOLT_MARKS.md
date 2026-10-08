# 결정: 볼트 추가 길이 / 마크 번호 형식 / 앵커·매입 마크

결정일: 2026-10-08 (KST). 작성: Grok Bot (Mus 요청). 근거는 웹 자료 + 로컬 원본(HS-STEEL) 파일 확인. 원본 파일은 git에 넣지 않았다(경로만 기록).
관련: `docs/STANDARDS_RESEARCH.md` (Claude-sonnet 조사, 옵션 도입), `docs/RULES_CATALOG.md` BL-*/NUM-*.

| # | 질문 | 결정 (엔진 기본값) | 신뢰도 | 덮어쓰기 |
|---|---|---|---|---|
| 1 | 조임길이에 더하는 길이: SCSS .dat 25/30/35 vs 단중.xlsx 30/35/40 | **볼트 종류별**. TS(S10T) M16 25 / M20 30 / M22 35 / M24 40 / M27 45 / M30 50, 육각 HTB(F10T/F8T) 30/35/40/45/50/55. 길이 = 올림((그립+추가)/5)×5 | 높음 | `boltLengthTable` = `by_bolt_set`(기본) / `kcs` / `ts_one_washer` |
| 2 | 마크 형식 C001 vs C1 | **0 채움, 자리수 = Numbering.dat 템플릿 폭**(C001 → 3자리). Numbering.dat 없으면 3자리 | 중상 | `markDigits` 1..6 (1 = 예전 "C1", 2 = "C01") |
| 3 | 매입(Embed) 조립 마크 EM vs Z | **EB** (원본 M80 EMBED 머리글 "EB01"). EM은 원본 근거 없음, Z는 GITA(기타) 머리글 | 중 | `MarkHeads[Embed]` / Numbering.dat `M83-EMBED--HD-BOX`, 또는 `markScheme=alt` → AB |

---

## 1. 볼트 추가 길이 — 볼트 종류별 (TS 25/30/35, HTB 30/35/40)

가설("TS는 와셔 1장이라 육각 HTB보다 짧다")을 따로 확인했다. 확인됨: 두 레거시 표는 서로 모순이 아니라 **각자 다른 볼트 세트**의 값이다.

### 근거
1. **KCS 14 31 25 표 2.1-5** (조임길이에 더하는 길이 = 너트 + 와셔 2장 + 나사산 3개): M16 30, M20 35, M22 40, M24 45, M27 50, M30 55. 표 주 2: *"다만 TS볼트의 경우에는 위의 값에서 와셔 1개의 두께를 뺀 길이를 적용한다."* 본문: 계산값에 가장 가까운 규격 길이 선택.
   - https://resource.midasuser.com/ko/blog/bridge/torque-management-for-high-strength-bolts
   - https://vvildnnan3.tistory.com/151
2. **국내 체결재 업체 표**: TS(토크쉬어) M16 25 / M20 30 / M22 35 / M24 40, 육각 고장력 30/35/40/45.
   - https://www.smfasteners.co.kr/bbs/board.php?bo_table=sub05_01&page=9&wr_id=30
3. **JASS 6 / 일본 제조사 TC볼트**: TC(=TS) 볼트 추가 길이 25/30/35/40(M27 45, M30 50), JIS 육각 고력볼트 30/35/40/45/50/55, 5mm 단위.
   - https://www.nipponsteel.com/product/construction/pdf/E001_10-02.pdf
   - https://hayamihyou.net/hi-tension-bolt/
4. **원본 HS-STEEL 파일 (로컬, git 미포함)**
   - `C:\HS-STEEL\HSSTEEL\attributes\SCSS-G20.dat` 등: 볼트가 명시적으로 `TS M20*60` 형식 → 조임길이+25/30/35, 5mm 올림으로 350/350행 재현.
   - `C:\HS-STEEL\HS03\Excel\XLSTART\단중.xlsx`: SCSS 시트 머리글은 일반 "고력볼트", `bolt` 시트 SET 중량 = 볼트 + 너트 + **와셔 2장** (HLCCT 963행 "HTB/N/2W", 965행 "TS/N/W") → 30/35/40은 2와셔 육각 세트 값. 700/700행이 +30/35/40 올림으로 재현.
   - `C:\HS-STEEL\HS03\견적용-2017\자재산출-2017.xlsm`: 이름 범위 `BOLTADDLEN`이 HTB / TS / TUB 열을 따로 둠 (원본 프로그램도 종류별로 분리).
   - `C:\HS-STEEL\HSSTEEL\block\BOLT-DATA-TABLE.dwg`의 S2 열(30/35/40/45/50/55)은 **나사 길이**이지 추가 길이가 아니다(혼동 주의).

### 반올림
원본 .dat/단중.xlsx는 모두 **5mm 올림**(ceil)으로 재현된다. KCS 본문은 "가장 가까운 길이", JFE 등은 2捨3入이지만, 원본 호환을 위해 올림 유지(짧은 볼트보다 안전).

### 코드
- `StandardOptions.DefaultBoltLengthTable` = `by_bolt_set` (TS/S10T → 1와셔 표, 그 외 → KCS 표). 볼트 이름 "TS …" → S10T (`Bolts.GradeOf`).
- `ts_one_washer` 표에 M27 45 / M30 50 추가 (이전에는 M24 행 40이 M27/M30에도 적용되던 버그).
- 예: 전단탭 TS M20, 그립 17 → 47 → **TS M20x50** (이전 기본 `kcs`는 55).

---

## 2. 마크 번호 형식 — 0 채움, Numbering.dat 폭 (기본 3자리 "C001")

### 근거
1. **원본 Numbering.dat (2019판, 새공사-설치용/중부/하눌쏘/작업 등)**: 모든 M83-*-HD-BOX 템플릿이 3자리 — `C001`, `G001`, `B001`, `R001`, `PU001`, `GT001`, `S001`, `H001`, `Z001`, `X001` … (`C:\HS-STEEL\...\attributes\Numbering.dat`). `m83-number001-btn` / `m83-number100-btn` 버튼(시작 번호 001 / 100).
2. **이전 M80 설정** (`AutoSaveLoad.M80`, 2018): `MC01`, `G01`, `EB01` … 2자리 0 채움. 즉 원본은 세대가 바뀌어도 항상 **0 채움**, 폭만 2→3.
3. **원본 사용설명서** (Hssteel2020사용자설명서 p22–27): 조립 XDATA에 "주자재마크"(설계 마크 `C1`, `SG1`)와 별도로 **"Assy명새형식"** `C07`, `G01`, `G02`가 기록됨. hs02.VLX 문자열 `'종류별 시작ASSY명 New'`, `'001A01 분석ASSY명 Old'`.
4. **템플릿 도면 새공사-2019.dwg**: 도면번호 D-000, S-000, 조립 예 `000A00` — 0 채움 관행.

`C1`, `SG1`처럼 0 채움 없는 마크는 **구조 설계도의 부재 마크**(설계 리스트 MARK, 레이어명)이며 제작 조립 마크와는 다른 개념이다(실제 프로젝트 구조도면 DWG 확인). 견적용 xlsm의 `SC1`/`SG1`도 설계 마크.

부수 효과: 0 채움이면 문자열 정렬이 번호 순서와 같다(C002 < C010). `Sheets.cs`는 마크를 ordinal 정렬한다.

### 코드
- `DetailRules.MarkDigits` (기본 3), `StandardOptions.FormatMark(head, n, digits)`; 999 초과 시 자리가 늘어날 뿐 잘리지 않음.
- `AssemblyTypes.DigitsFrom(numbering)`: HD-BOX 템플릿 끝 숫자 자리수의 최빈값. `DetailRules.From`와 `Workspace.Create`(attributes/Numbering.dat)가 적용.
- `floor_prefix`도 같은 폭: `2C001` (`markDigits=1`이면 `2C1`).
- 부품 마크(`P1`, `S1`)는 변경하지 않음.
- `SectionCatalog.Load`는 Numbering.dat를 단면표로 읽지 않도록 건너뜀(설정 파일).

---

## 3. 매입(Embed) 조립 마크 — EB

### 근거
1. **원본 M80 설정** `AutoSaveLoad.M80`: `("M80-EMBED--HD-TXT" . "EB01")` — 원본에 존재하는 유일한 EMBED 조립 머리글. 같은 파일의 GITA는 `Z01`.
2. **원본 M83 Numbering.dat**: EMBED 머리글 키 없음, `M83-GITA---HD-BOX` = `Z001`. 설명서: "Assy분석시 CL-3D 는 모두 **기타**로 처리" — Z는 기타(미분류) 머리글이지 매입물 머리글이 아님. EMBED 유형 설명: "인서트 또는 앙카와 하부철물을 별도 ASSY로 지정할 때 사용". 매입 단품명은 `000E`/`900E` (`ASSY-DWG-TABLE.dwg` MODULE-EMBED-USE), `M85-ASY-EMBED-DAN-MARK-BOX` "000".
3. **AB**는 원본에서 **앵커볼트 품목 표기**: `AB M20(L-740)`(자재산출-2017.xlsm), `AB/2N/W M25 (L=640)`(앙카주문서.dwg), 단중.xlsx "ANCH BOLT/3N/2W". 조립 마크가 아니라 볼트 규격명이라 legacy 기본으로 쓰면 BOM에서 혼동된다. (alt 체계는 AB 유지.)
4. **EP**는 미국 Tekla 환경의 embed plate 접두어로 국내 원본과 무관: https://support.tekla.com/article/us-envhelp-numbering-prefixes
5. **EM**은 원본/설명서/DWG 어디에도 근거가 없는 이전 엔진 임의값.

### 코드
- `AssemblyTypes.Prefix(Embed)` = `"EB"` (→ `EB001`).
- `NumberingKeys[Embed]` = `M83-EMBED--HD-BOX` (엔진 확장 키; 원본 M83에는 없음) — Numbering.dat에 넣으면 덮어씀. 없으면 `M80-EMBED--HD-TXT`(EB01) 값도 인정.
- `markScheme=alt` → `AB`.

---

## 덮어쓰는 방법

| 무엇 | MCP | 프로젝트 JSON (`rules`) | 자산 폴더 |
|---|---|---|---|
| 볼트 추가 길이 | `hs_project_options(name, boltLengthTable: "kcs")` (또는 `ts_one_washer`, `by_bolt_set`); `hs_project_new`/`hs_project_frame`도 같은 인자 | `"bolt_length_table": "kcs"` | — |
| 마크 자리수 | `hs_project_options(name, markDigits: 1)` (1..6; 0 = 그대로) | `"mark_digits": 1` | `attributes/Numbering.dat`의 `M83-*-HD-BOX` 템플릿 폭 (`C01` → 2) |
| 매입 머리글 | `markScheme: "alt"` → AB | `"mark_heads": {"embed": "EM"}` | `("M83-EMBED--HD-BOX" "EM001")` |

변경 후 모델을 다시 빌드한다. 기존 프로젝트 JSON에 `mark_digits`가 없으면 3자리가 적용된다(예전 마크를 유지하려면 `markDigits: 1`).

## Mus만 정할 수 있는 것
1. 자리수 2(M80 `C01`) vs 3(M83 `C001`): 2019 Numbering.dat를 따라 3을 기본으로 함.
2. 시작 번호 001 vs 100 (`m83-number100-btn`): 엔진은 1부터.
3. `floor_prefix`의 0 채움(`2C001` vs `2C1`): 같은 폭 적용, 필요하면 `markDigits`로.
4. 매입 머리글 EB(원본 M80) vs AB(현장에서 앵커 조립을 AB로 부르는 경우): EB 기본, alt=AB.
5. TS M27/M30 (45/50): JASS 6 기준, 국내 TS는 보통 M24까지라 실사용 드묾.
6. 반올림: 원본 호환 5mm 올림 vs KCS "가장 가까운 길이".