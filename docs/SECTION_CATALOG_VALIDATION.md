# Section catalog validation

기준일: 2026-10-03

이 단계는 `hs-steel-cad` 전체를 VERIFIED로 만드는 작업이 아니다.
`SectionTable` capability만 독립적으로 검증하기 위한 기반이다.

## 승격 전 필수 조건

- 숫자 파싱 실패를 0으로 대체하지 않는다.
- NaN/Infinity를 거부한다.
- 음수 치수, 0 이하 단중, 음수 도장면적, ACI 1..255 외 값을 격리한다.
- 짧은 행을 조용히 건너뛰지 않고 COLUMN_COUNT 오류로 기록한다.
- 오류에는 line number, column, raw value, raw line, error code를 남긴다.
- file load 시 CP949와 source SHA-256을 기록한다.
- read / accepted / quarantined row count를 항상 대조한다.
- 원본 자산 디렉터리가 없으면 PASS가 아니라 NOT_RUN이다.
- capability별 required file list를 별도로 넘겨 coverage를 계산한다.

## 공개 fixture와 private legacy asset 분리

`tests/HsSteel.Tests/Fixtures/H-BEAM-mini.dat`는 저장소에 포함된 작은 공개 fixture다.
CI에서 항상 실행된다.

실제 `C:\HS-STEEL\HSSTEEL\attributes` 자산은 저장소에 복사하지 않는다.
`HS_STEEL_LEGACY`가 없을 때 실제 자산 검증 상태는 NOT_RUN이다.

Section parser가 공개 fixture를 PASS해도 실제 HS-STEEL catalog가 VERIFIED라는 뜻은 아니다.
실자산 required set과 source hash가 확보된 뒤 별도 evidence를 생성해야 한다.

## 현재 capability 상태 의미

- public fixture PASS: parser logic TESTED
- private asset missing: native asset validation NOT_RUN
- private asset present + all required files + quarantine 0: asset parse candidate PASS
- modeling / drawing generation: 별도 capability이며 이 결과로 승격하지 않음
