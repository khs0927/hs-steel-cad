# Shear-tab default bolt rows (shallow beams)

`ModelBuilder.ShearTab` takes the bolt rows from the splice standard (`SpliceStandards.WebY`) when it has
an entry for the beam. Otherwise it uses `ShearTabLayout.Default`.

## The old default and why it was wrong

`n = max(1, ⌊(D − 2tf − 80) / 70⌋)` intervals → n + 1 rows, plate height 80 + 70n.

For shallow beams the `max(1, …)` forced two rows. H150×75×5×7 got a 150 mm plate, which is the full
beam depth. That puts the plate into the flanges and root fillets, whose clear web is 120 mm. The old rule
also ignored the fillet radius and the bolt size.

## The rule now (`src/HsSteel.Modeling/ShearTabLayout.cs`)

| Quantity | Rule | Source / assumption |
|---|---|---|
| Space available | clear web depth **T = D − 2(tf + r)** | the plate must not touch the flanges or the root fillets. r comes from the section table (0 if absent) |
| Edge distance e (vertical, plate edge → first/last hole) | max(40, e_min). e_min for a **sheared** edge: M16 28, M20 34, M22 38, M24 42, M27 48, M30 52 mm (> M30: 1.75d) | KDS 14 31 25 (KBC 2016 0710.3.4) = AISC 360-05 Table J3.4M. 40 mm is the shop default |
| Pitch p | max(70, 3d), rounded up to 5 mm | 3d is the preferred spacing (KDS 14 31 25 / AISC 360 J3.3; the absolute minimum is 2⅔d). 70 mm is the shop default |
| Rows | n = ⌊(T − 2e) / p⌋ + 1 | the plate height 2e + (n − 1)p is ≤ T and centred on the beam depth |
| n < 2 | warning in `ModelResult.Warnings` | conventional single-plate shear tabs use 2–12 rows (AISC Steel Construction Manual Part 10) |
| T < 2e | one centred row, plate = T, warning | the model stays complete. The engineer must change the connection (end plate, double angles, or a deeper beam) |

A splice-standard axis taller than T is also flagged (`ShearTabLayout.Check`).

This rule is a **detailing default, not a strength check**. Bolt shear, bearing, block shear and plate
flexure are not verified.

## Examples (M20)

| Section | T (mm) | Rows | Plate (mm) | Flag |
|---|---|---|---|---|
| H100×100×6×8 (r 10) | 64 | 1 (centred) | 64 | edge distance + rows |
| H150×75×5×7 (r 8) | 120 | 1 | 80 | rows |
| H200×100×5.5×8 (r 11) | 162 | 2 | 150 | — |
| H400×200×8×13 (r 16) | 342 | 4 | 290 | — |
| H600×200×11×17 (r 22) | 522 | 7 | 500 | — |

Tests: `tests/HsSteel.Tests/ShearTabLayoutTests.cs`. The unit tests run in CI. The model-level test needs
the legacy section catalog (`HS_STEEL_LEGACY`).
