# HS-STEEL 자산 대장 (요약판)

원본: C:\HS-STEEL (1,801 파일 / 39.5 MB). 파일 단위 대장은 P0에서 assets/manifest.json 으로 생성하며 합계가 1,801과 일치해야 한다.
변경(v0.3): 출력 관련 자산(Plot Styles *.ctb, allplot/*)은 '출력은 사용자 담당' 결정에 따라 reference 로만 유지한다. 도곽은 회사 도곽 블록을 입력으로 받는다.

| 원본 경로 | 수 | 종류 | 처분 | 신규 위치/용도 |
|---|---|---|---|---|
| `HSSTEEL/attributes/*.dat` | 26 | 형강·판·SCSS·데크·그레이팅·콘크리트 규격표, Project.dat | **ingest** | `HsSteel.Assets/sections/*.json` (전 열 보존: 규격, M1~M7, 단중, PAINT, 색상) |
| `attributes/` (루트, 2024) | — | 위 규격표의 구버전 | **reference** | 차이 리포트(diff) 생성 후 최신본 확정 근거 |
| `HSSTEEL/block/*.dwg` | 114 | 상세 블록(볼트·용접기호·마크·도곽 등) | **ingest** | ACadSharp로 읽어 `blocks/` 라이브러리 + 블록 메타(삽입점, 속성 태그) |
| `HSSTEEL/block/*.bak, .cdc, .err` | 9 | 백업/캐시/오류 로그 | excluded (백업·캐시) | 대장에만 기록 |
| `HSSTEEL/support/*.bmp, *.png` | 875 | 리본/툴바 아이콘 | **ingest** | 명령 아이콘 맵(향후 UI/팔레트) |
| `HSSTEEL/support/*.cuix, *.mnr, *.mnl, *._mn` | 6 | 메뉴·리본·로드 스크립트 | **reference** | 명령 카탈로그(447노드/351 바인딩, `orch/ui_cuix_spec.json`) → `hs://commands` |
| `HSSTEEL/support/*.atc, *.xtp` | 44 | 도구 팔레트 정의 | **ingest** | 팔레트 항목 → 블록/명령 매핑 |
| `HSSTEEL/support/*.lin, *.mln` | 2 | 선종류·다중선 스타일 | **ingest** | Drafting Standards (G6) |
| `HSSTEEL/support/*.fmp, *.pgp, *.xpg, *.DCL` | 4 | 글꼴 매핑·단축명령·대화상자 | **ingest**(fmp,pgp) / reference(DCL·xpg) | 글꼴 대체표, 단축명령 별칭, 대화상자 입력항목 → 툴 파라미터 명세 |
| `HSSTEEL/Icons/*.sld` | 416 | 슬라이드(단면·상세 미리보기 그림) | **ingest** | SLD→SVG 변환, 형강/상세 선택 미리보기 |
| `HSSTEEL/Icons/*.BMP, .DCL, .db, .log` | 146 | 아이콘·대화상자·썸네일캐시·로그 | ingest(BMP,DCL) / excluded(.db Thumbs, .log) | |
| `HSSTEEL/Plot Styles/*.ctb`, 각 새공사 `*.ctb` | 2+7 | 출력 스타일 | **ingest** | Plot(G5) 기본 CTB |
| `HSSTEEL/hs02.VLX`, `HS-DETAIL.VLX` | 2 | 본체 LISP(컴파일) | **reference** | 심볼·명령 목록(`fas-catalog`)만 명세로 사용, 코드 이식 없음 |
| `HSSTEEL/CSHSSTEELNET40.dll, CsMenuLoad40.dll, CsNewProject40.dll` | 3 | .NET 플러그인 | **reference** | 공개 명령·입출력 관찰 명세 |
| `HSSTEEL/CSROCKEY2013.dll, r4nd_class.dll`, `HsRockey/*` | 5 | 동글 인증 | **excluded** — 라이선스 메커니즘, 재구현 대상 아님 | 대장에만 기록 |
| `HSSTEEL/도움말/*.pdf, .xlsx, .JPG` | 6 | 사용 설명서·예시 | **reference** | 기능 명세·용어집·작도 규칙 출처 |
| `HSSTEEL/새공사-설치용/*` | 15 | 신규 공사 기본 세트(dat 8, dwg, ctb, xlsm, scr, exe, txt) | **ingest**(dat·dwg·ctb·xlsm) / reference(scr·exe) | 신규 Project 기본값·템플릿 도면 |
| `HSSTEEL/새공사-User0, 대아, 성우, 중부, 하눌쏘/*` | 60 | 실제 공사 세트(dat, dwg, xlsm, ctb, `.Mxx` 부재 데이터) | **reference(골든)** — 회사 프로파일로는 쓰지 않음 | E2E 정답지: 같은 입력 → 같은 BOM/견적/도면 |
| `HSSTEEL/작업/*` | 16 | 작업 샘플(dat, dwg, xlsm, txt) | **reference(골든)** | 회귀 테스트 |
| `HSSTEEL/ida.py, plot.log, 새 텍스트 문서.txt` | 3 | 분석 스크립트·로그·메모 | excluded(분석 부산물) / 메모는 reference | |
| `HS03/견적용-2017/자재산출-2017.xlsm`, `HS03/Excel/*.xlsx` | 2 | 자재산출·견적 로직·단가 | **ingest**(단가·할증·서식) + reference(수식 = 오라클) | `HsSteel.Quantities` 규칙, 9시트 BOM 양식 |
| `allplot/hjhvlx/*` (VLX, DLL, cuix, mnl, dcl, bmp) | 29 | 일괄출도 플러그인 | **reference**(동작) + ingest(bmp 아이콘) | Plot 모듈 명세: 도곽 탐지·용지·순서 |
| `allplot/CsHsPlot.exe, CsMultiPlotNet40/45.dll, *.config, *.bak` | 7 | 출도 실행기 | **reference** / .bak excluded | |
| `allplot/Allplot-setup/*` | 3 | 출도 설정 샘플 | **reference(골든)** | 출도 결과 비교 |
| 루트 `HS_STEEL_OFFLINE_*`, `*.scr`, `.anchor/` | 6 | 이전 분석 산출물 | excluded(분석 부산물) | |

> 위 수치는 폴더 단위 집계다. 실제 대장은 `assets/manifest.json`에 **파일 1개 = 1행**(경로, 크기, sha256, 처분, 사유, 대상)으로 생성하며,
