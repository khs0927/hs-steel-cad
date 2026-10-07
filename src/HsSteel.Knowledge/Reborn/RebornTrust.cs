namespace HsSteel.Knowledge.Reborn;

/// <summary>
/// Trust grades for ingested HS-STEEL_REBORN files. Every note cites ORCHESTRATION_PLAN.md (2026-09-26 corrected edition)
/// or a supersede record inside the file itself. Default for anything without a correction is 'unverified':
/// the plan states unmarked claims are original text, not independently checked.
/// </summary>
public static class RebornTrust
{
    public const string Verified = "verified";
    public const string Unverified = "unverified";
    public const string Refuted = "refuted";

    private const string Plan = "ORCHESTRATION_PLAN.md";

    public sealed record Grade(string Topic, string Trust, string Note);

    public static readonly IReadOnlyDictionary<string, Grade> Files = new SortedDictionary<string, Grade>(StringComparer.Ordinal)
    {
        ["orch/bom_schema.json"] = new("BOM 워크북 9시트 스키마 (자재산출서 BomList AssyList 표지 규격별합계)", Unverified,
            Plan + " §현재상태 BOM: the generated workbook had 3/9 sheets, 16 vs 5 columns, no BomList/AssyList — that refutes the *generated* workbook; this schema was read from the original 9-sheet xlsm and is not independently checked."),
        ["orch/bomlist_spec.json"] = new("BomList AssyList 시트 명세", Unverified,
            Plan + " agent #7 bom-list-sheet: '산출 없음' (no generated sheet). Spec itself states BomList/AssyList are header-only templates in the source xlsm."),
        ["orch/plate_spec.json"] = new("철판단품정리 판재 중량 명세", Unverified, Plan + " agent #6 plate-nest: '산출됨' only; no correction or verification recorded."),
        ["orch/cut_plan_spec.json"] = new("형강커팅플랜 절단 손실 명세 (cut plan)", Unverified, Plan + " agent #5 cut-plan: '산출됨' only; no verification recorded."),
        ["orch/plot_spec.json"] = new("출도 HSPLOTA3 A4 ALLPLOT 명세", Unverified, Plan + " agent #8 plot-adapter: '산출됨' only."),
        ["orch/quote_logic.json"] = new("견적 로직 (견적용-2017 xlsm)", Unverified, Plan + " agent #17 quote-report: '산출됨' only; file's own matches_original claim not independently re-run."),
        ["orch/fas_symbols.json"] = new("hs02.VLX FAS 심볼 함수 DCL", Unverified, Plan + " agent #12 fas-catalog: '산출됨'; string-scrape of compiled FAS, not executed."),
        ["orch/fas_symbols_summary.json"] = new("FAS 심볼 요약", Unverified, Plan + " agent #12 fas-catalog: '산출됨' only."),
        ["orch/command_triage.json"] = new("명령어 분류 v1 (집계)", Refuted,
            Plan + " §C: v1 holds only 4 aggregate numbers, 0 per-command rows, and 283+19+19=321 ≠ 302 — arithmetic mismatch; superseded by command_triage_v2.json."),
        ["orch/command_triage_v2.json"] = new("명령어 분류 v2 302 전수", Unverified,
            Plan + " §C: v2 classifies all 302 individually (3/144/117/38 undetermined, self-sum checked) — complete but classifications not independently verified."),
        ["orch/dat_full.json"] = new("attributes dat 전수 파싱", Unverified, Plan + " agent #2 dat-attrs: '산출됨 (파일 수는 정정 참조)'; our own section tables are the authoritative parse."),
        ["orch/dwg_blocks.json"] = new("DWG 블록 v1", Refuted,
            "orch/dwg_blocks_v2.json supersede_reason: v1 'top' counted ENTITIES not blocks, no errors key, 113 vs 114 files, generator not reproducible."),
        ["orch/dwg_blocks_v2.json"] = new("DWG 블록 v2 DXF 엔티티 집계", Unverified,
            "Not covered by " + Plan + "; v2 carries its own claim_audit and dwg/dxf reconciliation (113 of 114, '개선스켈럽 현장 FW.dwg' missing). Basename agreement with our block table is recorded as graph evidence."),
        ["orch/ui_cuix_spec.json"] = new("CUIX 메뉴 리본 명령 바인딩", Unverified,
            Plan + " agent #18: 447 nodes / 351 bindings, cuix 678/678 parse match; shortcuts NOT recoverable (AcceleratorRoot.cui empty)."),
        ["orch/template_manifest.json"] = new("새공사 템플릿 속성", Unverified, Plan + " agent #10 template-extract: '산출됨' only."),
        ["orch/net_asm.json"] = new(".NET 어셈블리 CSHSSTEELNET40 메타데이터", Unverified,
            Plan + " agent #13 net-asm: '산출물 부재' as of 2026-09-26; this file appeared later and is outside the plan's corrections."),
        ["orch/lisp_dialect.json"] = new("ZWCAD AutoLISP 방언 매핑", Unverified, Plan + " agent #11 lisp-compat: '산출됨'; bridge itself best-of-3 (1 run 0/5)."),
        ["orch/weight_audit.json"] = new("단중 검증 0.00kg (순환)", Refuted,
            Plan + " §B: circular validation — all 944 rows sourced from 단중.xlsx!HLCCT and compared against the same 단중.xlsx; theory path (area×7.85) executed 0 times."),
        ["orch/spec_coverage_v2.json"] = new("규격 파서 커버리지 v2", Unverified, "Supersedes spec_coverage.json per its own supersede_reason; not covered by " + Plan + "."),
        ["orch/_integration.json"] = new("통합 검증 재생성본 17/18", Unverified,
            Plan + " §integration: regenerated 17/18 ready; still lists claims_without_evidence (e.g. bridge_v2_diff tested:true)."),
        ["orch/_integration_20260917_snapshot.json"] = new("통합 검증 원본 스냅샷 (수기)", Refuted,
            Plan + " §integration: hand-written file — broken key 'core thore_results', generator absent, 'ready' was constant 0."),
        ["orch/zwcad_stability.json"] = new("ZWCAD 안정성 주장", Refuted,
            Plan + " §ZWCAD: stable:true/120s unproven — 120s is the script loop cap; ZWCAD self-terminated at 903s."),
        ["catalog/commands.json"] = new("명령 카탈로그 302개", Unverified, Plan + " §C: the count of 302 commands is correct; per-command texts are scraped from cuix help strings."),
        ["catalog/catalog_summary.json"] = new("명령 카탈로그 요약 기능그룹", Unverified, Plan + " §C: count correct; grouping not independently checked."),
        ["catalog/feature_groups.json"] = new("기능 그룹", Unverified, Plan + " §C: count correct; grouping not independently checked."),
        ["work/fas_strings.json"] = new("FAS 문자열 덤프", Unverified, "Raw string scrape feeding agent #12 (" + Plan + "); no correction recorded."),
        ["work/fas_module_index.json"] = new("FAS 모듈 인덱스 (VLX 오프셋)", Unverified, "Raw VLX container index feeding agent #12 (" + Plan + ")."),
        ["materials/weight_table.json"] = new("단중표 944행 (단중.xlsx HLCCT)", Unverified,
            Plan + " §B: data extracted but validation was circular; theory path never run → 미검증. Independent cross-check vs our section table recorded in meta 'reborn_weight_crosscheck'."),
        ["materials/scss_bolt_tables.json"] = new("SCSS 볼트 표", Unverified, "Part of B (자재 DB) per " + Plan + " — validation circular/미검증."),
        ["materials/estimate_2017.json"] = new("견적 2017 추출", Unverified, "Part of B/quote (" + Plan + ") — no independent verification recorded."),
    };
}
