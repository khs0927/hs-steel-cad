# hs-steel-cad

HS-STEEL을 클린룸으로 재구축한 철골 도면 자동 생성 엔진입니다(C#/.NET 10, MCP). 이후 power-cad-mcp에 병합할 예정입니다.

- 설계: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) · 자산 대장: [docs/ASSET_LEDGER.md](docs/ASSET_LEDGER.md)
- 테스트: `dotnet test` (원본 자산 경로: 환경변수 `HS_STEEL_LEGACY`, 기본값 `C:\HS-STEEL\HSSTEEL`)
- 데모: `dotnet run --project src/HsSteel.Mcp -- --demo out/demo_B1.dxf`
- MCP(stdio): `dotnet run --project src/HsSteel.Mcp`
