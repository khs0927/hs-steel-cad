# Legacy `.Mxx` format (reverse-engineered 2026-10-08)

Sources: `C:\HS-STEEL\HSSTEEL\새공사-*\attributes\*.M??` (27 files, 560-1700 bytes) and
`작업\`. Not copied into the repo.

## KEY FINDING: `.Mxx` files are NOT member files
They are per-macro **dialog option stores** (AutoLISP association lists) written by the
HS-STEEL AutoCAD macros (`AutoSaveLoad.M34` = macro 34 settings, `For-Macro70.M77`, ...).
They contain no marks, specs, lengths, quantities or weights. Member geometry lives only
in the `.dwg` (block/attribute entities), and the `BOM자재산출서.xlsm` workbooks in all 7
project sets are **empty templates** (header rows only; 자재산출서/규격별합계/AssyList/
형강단품정리 have 0 data rows; 사용자 sheet = user settings). Therefore there is no
independent member/weight oracle in `C:\HS-STEEL`; member/weight golden tests are
impossible until a populated BOM export or a DWG with HS block attributes is supplied.

## Layout (confidence)
- Encoding CP949, CRLF (medium: convention only, several variants).
- Line 1: stamp `NNNNNNNNN-yyyyMMddHHmm` (high on date part; the numeric prefix, e.g.
  749328975 / 742398455 / 40518563, is likely a machine/licence id: medium-low).
- Lines 2..n: one `("KEY" . "string")` or `("KEY" n)` / `("KEY" a b)` (medium: convention only, several variants). Dotted pair =
  string value, undotted = list of numbers (DCLPTXY = dialog x,y position, -1 -1 default).
- Key grammar `M{nn}-{NAME}-{TYPE}` (medium): TYPE TOG = toggle "0"/"1", BOX = edit box
  (text/number), POP = popup index, CHK = checkbox, LEN/GON/DEG = length/angle numerics.
  `{nn}` equals the file extension number (AutoSaveLoad.M34 -> M34-... or M34RAFTER-TOG or MACRO21-ABOLT) (medium: convention only, several variants).
- Wildcard lists such as `"PU*,GT*"`, `"BASE *,USER*"` are AutoCAD name filters (medium: convention only, several variants).
- Related `.dat` files (Project.dat, Numbering.dat, Allplot_set.dat) use the same stamp +
  alist format with unprefixed keys; Project.dat holds PROJECT/SUBPRO/ENDUSER/CONAME/
  TITLE, SWS (default steel grade, e.g. SS275), SHOLE, ENDGAGE (bolt-hole defaults).

## Mapping to Modeling
None possible for members. Useful derived defaults only: Project.dat SWS, SHOLE,
ENDGAGE, SCALLOP, STFFGAP could seed a Modeling project's defaults (public API for
project defaults not verified; Modeling not edited).
