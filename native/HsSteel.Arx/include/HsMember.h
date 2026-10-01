// HsMember - ObjectARX custom entity for a steel member (P5).
// Stores the same data as HsSteel.Domain.Member (mark, section, length, holes, scallop)
// and draws itself from it. Editing a grip or a property regenerates the geometry, and a
// reactor notifies the .NET layer (HsSteel.Drafting) to refresh the shop detail and BOM.
// Requires: ObjectARX SDK for AutoCAD 2027 + MSVC (Visual Studio 2022 v143, x64).
#pragma once
#include "dbmain.h"

class HsMember : public AcDbEntity
{
public:
    ACRX_DECLARE_MEMBERS(HsMember);

    HsMember();
    ~HsMember() override = default;

    // --- data (mirrors HsSteel.Domain.Member) ---
    Acad::ErrorStatus setMark(const ACHAR* mark);
    const ACHAR* mark() const;
    Acad::ErrorStatus setSection(const ACHAR* spec, double h, double b, double tw, double tf);
    Acad::ErrorStatus setAxis(const AcGePoint3d& start, const AcGePoint3d& end);
    double length() const;

    // --- AcDbObject persistence ---
    Acad::ErrorStatus dwgInFields(AcDbDwgFiler* filer) override;
    Acad::ErrorStatus dwgOutFields(AcDbDwgFiler* filer) const override;
    Acad::ErrorStatus dxfInFields(AcDbDxfFiler* filer) override;
    Acad::ErrorStatus dxfOutFields(AcDbDxfFiler* filer) const override;

protected:
    // --- AcDbEntity graphics / editing ---
    Adesk::Boolean subWorldDraw(AcGiWorldDraw* mode) override;
    Acad::ErrorStatus subGetGripPoints(AcGePoint3dArray& grips, AcDbIntArray& osnapModes, AcDbIntArray& geomIds) const override;
    Acad::ErrorStatus subMoveGripPointsAt(const AcDbIntArray& indices, const AcGeVector3d& offset) override;
    Acad::ErrorStatus subTransformBy(const AcGeMatrix3d& xform) override;
    Acad::ErrorStatus subExplode(AcDbVoidPtrArray& entitySet) const override; // proxy-safe fallback: plain lines

private:
    static constexpr Adesk::Int16 kVersion = 1;
    AcString m_mark;
    AcString m_spec;
    double m_h = 0, m_b = 0, m_tw = 0, m_tf = 0;
    AcGePoint3d m_start, m_end;
};
