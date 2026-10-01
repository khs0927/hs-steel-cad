# HsSteel.Arx (C++, P5)

ObjectARX custom entity `HsMember`: a steel member that draws itself from its data, supports grip edits,
and notifies the .NET layer to regenerate shop details and quantities.

Status: interface only (`include/HsMember.h`). Build prerequisites not present on this PC yet:
- Visual Studio 2022 Build Tools with "Desktop development with C++" (MSVC v143, x64)
- ObjectARX SDK for AutoCAD 2027 (Autodesk account download)

Until P5, the same behaviour is achieved in C# with XData on plain entities + regeneration.
Saved drawings must stay readable without this module: `subExplode` produces plain geometry and the
entity is registered with proxy graphics.
