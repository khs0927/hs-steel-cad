# HS-STEEL 지식 데이터 보관 규칙

DB·임베딩·모델은 용량과 회사 데이터 때문에 git에 넣지 않는다(`out/`은 gitignore). 대신 버전별 폴더로 보관한다.

| 위치 | 용도 |
|---|---|
| `C:\CODE\hs-steel-cad\out\` | 작업 사본 (`build-db`가 여기에 생성) |
| `D:\HS-STEEL-KG\` | 로컬 보관본 (버전별) |
| Google Drive `HS-STEEL-KG/` | 원격 보관본 (로컬과 같은 구조) |

## 폴더 구조

```
HS-STEEL-KG/
  README.md                         ← 이 문서
  db/<날짜>_<커밋>/                 hs_assets.db, SHA256SUMS.txt, build_info.json
  db/latest/                        최신 버전 사본 (power-cad는 이걸 HS_ASSETS_DB로 지정)
  knowledge/<날짜>_<커밋>/          chunks.jsonl, embeddings.npy, embeddings_ids.json, coverage.json, vba_procedures.jsonl
  models/multilingual-e5-small-onnx/ 오프라인 질의 임베딩 모델 (모델 단위로 1부만)
  manifests/<날짜>_<커밋>/          manifest.json(HS-STEEL 1,801), reborn_manifest.json(REBORN 16,415)
  verification/<날짜>_<커밋>/       실 AutoCAD 스모크, Graph RAG 평가 결과
```

버전 이름의 커밋은 그 DB를 만든 `hs-steel-cad` 커밋이다. 같은 커밋이면 DB는 바이트 동일하게 다시 만들어진다.

## 갱신 절차

```
dotnet run --project src/HsSteel.Knowledge -- build-db
powershell -File tools\publish_data.ps1 -Upload
```

`-Upload`가 실패하면(토큰 만료) `rclone config reconnect gdrive:` 후 다시 실행한다.
보관하지 않는 것: `out/probe_k3`, `out/explore_results`의 대용량 페이로드(재생성 가능한 실험 산출물).
