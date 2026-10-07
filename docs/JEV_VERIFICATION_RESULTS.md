# JEV 검증 결과 (2026-10-08)

- **실행 시각**: 2026-10-08 01:52:04 +09:00 (Asia/Seoul)
- **머신**: DESKTOP-KTQHS1I (`a0056d74-b107-4102-ac4c-a2c22ebcd7c4`)
- **모델**: `jev-1.13-free` (비용 0)
- **AutoCAD**: **이번 라운드 불필요** (라이브 `create_many`/XData 스모크 때만 이후 필요)
- **주 경로**: **MCP `user-jev`** (커서 커넥터 → SystemOne) — 7/7 실호출
- **보조 경로**: 직접 SystemOne API `C:\code\_ops\jev-verify-all.mjs` (다중 noul, smoke와 동일 패턴)
- **jevgrep egress**: 생략 (`jg` 미설치; 사설 소스 egress 금지)
- **기계 판독 JSON**: `out/explore_results/jev_verification_20261008.json`, `docs/JEV_VERIFICATION_RESULTS.json`

---

## 1. JEV 7종 기능 — **7/7 PASS** (MCP)

| # | 도구 | 결과 | 판정 |
|---|---|---|---|
| 1 | `jev_decide` | `allow` (confidence 0.98, allow≈0.99) | PASS |
| 2 | `jev_screen` | benign `allow` 0.98 / danger `deny` 0.01 | PASS |
| 3 | `jev_classify` | `기능요청` (0.81) — 「도면에 H빔 100개를 일괄 생성해줘」 | PASS |
| 4 | `jev_tool_route` | `병렬 호출` (0.83) | PASS |
| 5 | `jev_noul` | 「4종 정상 동작」 확률 **0.93** | PASS |
| 6 | `jev_verify` | `verified: false` (0.01) — 증거 빈약 시 거짓 판정 | PASS |
| 7 | `jev_rerank` | X(0.98) > Y(0.97) > Z(0.06) | PASS |

### 보조 API 스크립트 요약 (`jev-verify-all.mjs`)

decide=allow(0.92), screen benign 0.97 / danger 0.01, classify=기능요청(0.91), tool_route=병렬(0.72), noul=0.85, verify false(0.02), rerank X≈Y≫Z — 방향성 MCP와 일치.

---

## 2. 결함 재검증

| # | 결함 | 판정 |
|---|---|---|
| 1 | 빈 `state` | **PASS** — `requires non-empty string argument 'state'` |
| 2 | screen 점수 | **PASS** — benign 0.98 / danger 0.01 |
| 3 | rerank 순서 반전 (Z,Y,X) | **PASS (MCP)** — 여전히 X>Y>Z (0.98/0.97/0.05). API 다중 noul은 X≈Y 동점(0.97)에서 정렬 타이브레이크 변동 가능 |

---

## 3. rerank 스모크 (n=10)

`
node C:\code\jev-rerank-smoke.mjs "<criteria>" C:\code\jev-rerank-candidates.json 10
`

| 지표 | 결과 |
|---|---|
| **top_counts** | **알고리즘 X: O(1) → 10/10** |
| X 점수 | 0.93~0.94 (jitter 0.01) |
| Y 점수 | 0.34~0.41 (criteria가 X 선호 → 구 리포트 near-tie보다 낮음) |
| Z 점수 | 0.05~0.06 |
| 순위 | 항상 X>Y>Z |
| 비용 | 0 |

원본 출력: `out/explore_results/jev_rerank_smoke_20261008.json`

---

## 4. 스크립트 / 산출물

| 경로 | 설명 |
|---|---|
| `C:\code\_ops\jev-verify-all.mjs` | 7능력 + order-swap SystemOne 검증 스크립트 |
| `out/explore_results/jev-verify-all.mjs` | 동일 사본 |
| `out/explore_results/jev_verify_all_api_20261008.json` | API 경로 원시 결과 |
| `out/explore_results/jev_rerank_smoke_20261008.json` | smoke n=10 |
| `C:\code\jev-test-report-20261008.md` | 동일 요약 사본 (레포 밖) |

---

## 5. 최종

- **JEV 7/7 PASS** (MCP 주 경로)
- **smoke top_counts: X 10/10**
- AutoCAD 이번 라운드 **불필요**; 다음 라이브 create_many/XData 때만 필요