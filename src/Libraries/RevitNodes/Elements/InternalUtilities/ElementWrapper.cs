using System;
using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;
using Revit.Elements.Views;
using DB = Autodesk.Revit.DB;

namespace Revit.Elements
{
    /// <summary>
    /// Element wrapper supplies tools for wrapping Autodesk.Revit.DB.Element types
    /// in their associated Revit.Elements.Element wrapper
    /// </summary>
    [SupressImportIntoVM]
    public static class ElementWrapper
    {
        /// <summary>
        /// If possible, wrap the element in a DS type
        /// </summary>
        /// <param name="ele"></param>
        /// <param name="isRevitOwned">Whether the returned object should be revit owned or not</param>
        /// <returns></returns>
        public static Element ToDSType(this DB.Element ele, bool isRevitOwned)
        {
            // cast to dynamic to dispatch to the appropriate wrapping method
            //dynamic dynamicElement = ele;
            //return ElementWrapper.Wrap(dynamicElement, isRevitOwned);

            switch (ele)
            {
                case DB.Panel panel:
                    {
                        if (AdaptiveComponentInstanceUtils.IsAdaptiveFamilySymbol(panel.Symbol))
                        {
                            return AdaptiveComponent.FromExisting(panel, isRevitOwned);
                        }

                        return CurtainPanel.FromExisting(panel, isRevitOwned);
                    }

                case DB.Mullion mul:
                    return Mullion.FromExisting(mul, isRevitOwned);

                case DB.FamilyInstance fi:
                    {
                        if (AdaptiveComponentInstanceUtils.HasAdaptiveFamilySymbol(fi))
                        {
                            return AdaptiveComponent.FromExisting(fi, isRevitOwned);
                        }

                        if (fi.StructuralType != DB.Structure.StructuralType.NonStructural &&
                            fi.StructuralType != DB.Structure.StructuralType.Footing)
                        {
                            return StructuralFraming.FromExisting(fi, isRevitOwned);
                        }

                        return FamilyInstance.FromExisting(fi, isRevitOwned);
                    }

                case DB.Area ar:
                    return Area.FromExisting(ar, isRevitOwned);

                case DB.Ceiling ce:
                    return Ceiling.FromExisting(ce, isRevitOwned);

                case DB.CeilingType cet:
                    return CeilingType.FromExisting(cet, isRevitOwned);

                case DB.DirectShape ds:
                    return DirectShape.FromExisting(ds, isRevitOwned);

                case DB.DividedPath dp:
                    return DividedPath.FromExisting(dp, isRevitOwned);

                case DB.FamilySymbol famType:
                    return FamilyType.FromExisting(famType, isRevitOwned);

                case DB.FloorType flrType:
                    return FloorType.FromExisting(flrType, isRevitOwned);

                case DB.ModelTextType mTxtType:
                    return ModelTextType.FromExisting(mTxtType, isRevitOwned);

                case DB.WallType walType:
                    return WallType.FromExisting(walType, isRevitOwned);

                case DB.ToposolidType toposolidType:
                    return ToposolidType.FromExisting(toposolidType, isRevitOwned);

                case DB.TextNoteType txtType:
                    return TextNoteType.FromExisting(txtType, isRevitOwned);

                case DB.FilledRegionType fillRegType:
                    return FilledRegionType.FromExisting(fillRegType, isRevitOwned);

                case DB.DimensionType dimType:
                    return DimensionType.FromExisting(dimType, isRevitOwned);

                case DB.RoofType roofType:
                    return RoofType.FromExisting(roofType, isRevitOwned);

                /* put element types subclasses above this line */
                case DB.ElementType elType:
                    return ElementType.FromExisting(elType, isRevitOwned);

                case DB.DividedSurface div:
                    return DividedSurface.FromExisting(div, isRevitOwned);

                case DB.Family fam:
                    return Family.FromExisting(fam, isRevitOwned);

                case DB.Floor flr:
                    return Floor.FromExisting(flr, isRevitOwned);

                case DB.Form frm:
                    return Form.FromExisting(frm, isRevitOwned);

                case DB.FreeFormElement free:
                    return FreeForm.FromExisting(free, isRevitOwned);

                case DB.Grid grd:
                    return Grid.FromExisting(grd, isRevitOwned);

                case DB.Group grp:
                    return Group.FromExisting(grp, isRevitOwned);

                case DB.Level lvl:
                    return Level.FromExisting(lvl, isRevitOwned);

                case DB.ModelCurve mCrv:
                    return ModelCurve.FromExisting(mCrv, isRevitOwned);

                case DB.CurveByPoints cPts:
                    return CurveByPoints.FromExisting(cPts, isRevitOwned);

                case DB.ModelText mTxt:
                    return ModelText.FromExisting(mTxt, isRevitOwned);

                case DB.ReferencePlane rPlane:
                    return ReferencePlane.FromExisting(rPlane, isRevitOwned);

                case DB.ReferencePoint rPt:
                    return ReferencePoint.FromExisting(rPt, isRevitOwned);

                case DB.SketchPlane skPlane:
                    return SketchPlane.FromExisting(skPlane, isRevitOwned);

                case DB.Wall wall:
                    return Wall.FromExisting(wall, isRevitOwned);

                case DB.View3D view3d:
                    {
                        if (!view3d.IsTemplate)
                        {
                            if (view3d.IsPerspective)
                                return PerspectiveView.FromExisting(view3d, isRevitOwned);
                            else
                                return AxonometricView.FromExisting(view3d, isRevitOwned);
                        }
                        else if (view3d.IsTemplate)
                        {
                            return View3DTemplate.FromExisting(view3d, isRevitOwned);
                        }
                        return null;
                    }

                case DB.ViewPlan viewPlan:
                    {
                        switch (viewPlan.ViewType)
                        {
                            case ViewType.CeilingPlan:
                                return CeilingPlanView.FromExisting(viewPlan, isRevitOwned);
                            case ViewType.FloorPlan:
                                return FloorPlanView.FromExisting(viewPlan, isRevitOwned);
                            case ViewType.EngineeringPlan:
                                return StructuralPlanView.FromExisting(viewPlan, isRevitOwned);
                            case ViewType.AreaPlan:
                                return AreaPlanView.FromExisting(viewPlan, isRevitOwned);
                            default:
                                return UnknownElement.FromExisting(viewPlan, true);
                        }
                    }

                case DB.ViewSection viewSection:
                    return SectionView.FromExisting(viewSection, isRevitOwned);

                case DB.ViewSchedule viewSched:
                    return ScheduleView.FromExisting(viewSched, isRevitOwned);

                case DB.ViewSheet sheet:
                    return Sheet.FromExisting(sheet, isRevitOwned);

                case DB.ViewDrafting viewDraft:
                    return DraftingView.FromExisting(viewDraft, isRevitOwned);

                case DB.Architecture.TopographySurface topoSrf:
                    return Topography.FromExisting(topoSrf, isRevitOwned);

                case DB.Toposolid topoSol:
                    return Toposolid.FromExisting(topoSol, isRevitOwned);

                /* put view subclasses above this line */
                case DB.View view:
                    {
                        if (view.ViewType == ViewType.Legend)
                        {
                            return Legend.FromExisting(view, isRevitOwned);
                        }
                        return UnknownElement.FromExisting(view, true);
                    }

                case DB.Dimension dim:
                    return Dimension.FromExisting(dim, isRevitOwned);

                case DB.FilledRegion fillReg:
                    return FilledRegion.FromExisting(fillReg, isRevitOwned);

                case DB.FillPatternElement fillPat:
                    return FillPatternElement.FromExisting(fillPat, isRevitOwned);

                case DB.LinePatternElement linePat:
                    return LinePatternElement.FromExisting(linePat, isRevitOwned);

                case DB.TextNote txtNote:
                    return TextNote.FromExisting(txtNote, isRevitOwned);

                case DB.IndependentTag tag:
                    return Tag.FromExisting(tag, isRevitOwned);

                case DB.Revision rev:
                    return Revision.FromExisting(rev, isRevitOwned);

                case DB.RevisionCloud revCloud:
                    return RevisionCloud.FromExisting(revCloud, isRevitOwned);

                case DB.ParameterFilterElement parFilter:
                    return Revit.Filter.ParameterFilterElement.FromExisting(parFilter, isRevitOwned);

                case DB.Architecture.Room room:
                    return Room.FromExisting(room, isRevitOwned);

                case DB.DetailCurve dCrv:
                    return DetailCurve.FromExisting(dCrv, isRevitOwned);

                case DB.ImportInstance inst:
                    return ImportInstance.FromExisting(inst, isRevitOwned);

                case DB.GlobalParameter globalParam:
                    return GlobalParameter.FromExisting(globalParam, isRevitOwned);

                case DB.FaceWall fWall:
                    return FaceWall.FromExisting(fWall, isRevitOwned);

                case DB.CurtainSystem curtainSys:
                    return CurtainSystem.FromExisting(curtainSys, isRevitOwned);

                case DB.Material mat:
                    return Material.FromExisting(mat, isRevitOwned);

                case DB.Analysis.PathOfTravel pot:
                    return PathOfTravel.FromExisting(pot, isRevitOwned);

                case DB.Viewport vPort:
                    return Viewport.FromExisting(vPort, isRevitOwned);

                case DB.ElevationMarker elevMaker:
                    return ElevationMarker.FromExisting(elevMaker, isRevitOwned);

                case DB.Mechanical.Space space:
                    return Space.FromExisting(space, isRevitOwned);

                case DB.RoofBase roof:
                    return Roof.FromExisting(roof, isRevitOwned);

                case DB.ScheduleSheetInstance sched:
                    return ScheduleOnSheet.FromExisting(sched, isRevitOwned);

                case DB.AppearanceAssetElement ast:
                    return AppearanceAssetElement.FromExisting(ast, isRevitOwned);

                case DB.RevitLinkInstance linkIst:
                    return LinkInstance.FromExisting(linkIst, isRevitOwned);

                default:
                    return UnknownElement.FromExisting(ele, isRevitOwned);
            }
        }

        #region Wrap methods

        [Obsolete("Please use ToDsType instead")]
        public static UnknownElement Wrap(Autodesk.Revit.DB.Element element, bool isRevitOwned)
        {
            return UnknownElement.FromExisting(element, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static AbstractFamilyInstance Wrap(Autodesk.Revit.DB.FamilyInstance ele, bool isRevitOwned)
        {
            if (AdaptiveComponentInstanceUtils.HasAdaptiveFamilySymbol(ele))
            {
                return AdaptiveComponent.FromExisting(ele, isRevitOwned);
            }

            if (ele.StructuralType != Autodesk.Revit.DB.Structure.StructuralType.NonStructural &&
                ele.StructuralType != Autodesk.Revit.DB.Structure.StructuralType.Footing)
            {
                return StructuralFraming.FromExisting(ele, isRevitOwned);
            }

            return FamilyInstance.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Area Wrap(Autodesk.Revit.DB.Area ele, bool isRevitOwned)
        {
            return Area.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Ceiling Wrap(Autodesk.Revit.DB.Ceiling ele, bool isRevitOwned)
        {
            return Ceiling.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static CeilingType Wrap(Autodesk.Revit.DB.CeilingType ele, bool isRevitOwned)
        {
            return CeilingType.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static DirectShape Wrap(Autodesk.Revit.DB.DirectShape ele, bool isRevitOwned)
        {
            return DirectShape.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static DividedPath Wrap(Autodesk.Revit.DB.DividedPath ele, bool isRevitOwned)
        {
            return DividedPath.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ElementType Wrap(Autodesk.Revit.DB.ElementType elementType, bool isRevitOwned)
        {
            return ElementType.FromExisting(elementType, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static DividedSurface Wrap(Autodesk.Revit.DB.DividedSurface ele, bool isRevitOwned)
        {
            return DividedSurface.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Family Wrap(Autodesk.Revit.DB.Family ele, bool isRevitOwned)
        {
            return Family.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static FamilyType Wrap(Autodesk.Revit.DB.FamilySymbol ele, bool isRevitOwned)
        {
            return FamilyType.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Floor Wrap(Autodesk.Revit.DB.Floor ele, bool isRevitOwned)
        {
            return Floor.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static FloorType Wrap(Autodesk.Revit.DB.FloorType ele, bool isRevitOwned)
        {
            return FloorType.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Form Wrap(Autodesk.Revit.DB.Form ele, bool isRevitOwned)
        {
            return Form.FromExisting(ele, isRevitOwned);
        }

        [SupressImportIntoVM]
        [Obsolete("Please use ToDsType instead")]
        public static FreeForm Wrap(Autodesk.Revit.DB.FreeFormElement ele, bool isRevitOwned)
        {
            return FreeForm.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Grid Wrap(Autodesk.Revit.DB.Grid ele, bool isRevitOwned)
        {
            return Grid.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Group Wrap(Autodesk.Revit.DB.Group ele, bool isRevitOwned)
        {
            return Group.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Level Wrap(Autodesk.Revit.DB.Level ele, bool isRevitOwned)
        {
            return Level.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ModelCurve Wrap(Autodesk.Revit.DB.ModelCurve ele, bool isRevitOwned)
        {
            return ModelCurve.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static CurveByPoints Wrap(Autodesk.Revit.DB.CurveByPoints ele, bool isRevitOwned)
        {
            return CurveByPoints.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ModelText Wrap(Autodesk.Revit.DB.ModelText ele, bool isRevitOwned)
        {
            return ModelText.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ModelTextType Wrap(Autodesk.Revit.DB.ModelTextType ele, bool isRevitOwned)
        {
            return ModelTextType.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ReferencePlane Wrap(Autodesk.Revit.DB.ReferencePlane ele, bool isRevitOwned)
        {
            return ReferencePlane.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ReferencePoint Wrap(Autodesk.Revit.DB.ReferencePoint ele, bool isRevitOwned)
        {
            return ReferencePoint.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static SketchPlane Wrap(Autodesk.Revit.DB.SketchPlane ele, bool isRevitOwned)
        {
            return SketchPlane.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Wall Wrap(Autodesk.Revit.DB.Wall ele, bool isRevitOwned)
        {
            return Wall.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static WallType Wrap(Autodesk.Revit.DB.WallType ele, bool isRevitOwned)
        {
            return WallType.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static View3D Wrap(Autodesk.Revit.DB.View3D view, bool isRevitOwned)
        {
            if (!view.IsTemplate)
            {
                if (view.IsPerspective)
                    return PerspectiveView.FromExisting(view, isRevitOwned);
                else
                    return AxonometricView.FromExisting(view, isRevitOwned);
            }
            else if (view.IsTemplate)
            {
                return View3DTemplate.FromExisting(view, isRevitOwned);
            }
            return null;
        }

        [Obsolete("Please use ToDsType instead")]
        public static Element Wrap(Autodesk.Revit.DB.ViewPlan view, bool isRevitOwned)
        {
            switch (view.ViewType)
            {
                case ViewType.CeilingPlan:
                    return CeilingPlanView.FromExisting(view, isRevitOwned);
                case ViewType.FloorPlan:
                    return FloorPlanView.FromExisting(view, isRevitOwned);
                case ViewType.EngineeringPlan:
                    return StructuralPlanView.FromExisting(view, isRevitOwned);
                case ViewType.AreaPlan:
                    return AreaPlanView.FromExisting(view, isRevitOwned);
                default:
                    return UnknownElement.FromExisting(view, true);
            }
        }

        [Obsolete("Please use ToDsType instead")]
        public static SectionView Wrap(Autodesk.Revit.DB.ViewSection view, bool isRevitOwned)
        {
            return SectionView.FromExisting(view, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ScheduleView Wrap(Autodesk.Revit.DB.ViewSchedule view, bool isRevitOwned)
        {
            return ScheduleView.FromExisting(view, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Element Wrap(Autodesk.Revit.DB.View view, bool isRevitOwned)
        {
            switch (view.ViewType)
            {
                case ViewType.Legend:
                    return Legend.FromExisting(view, isRevitOwned);
                default:
                    return UnknownElement.FromExisting(view, true);
            }
        }

        [Obsolete("Please use ToDsType instead")]
        public static Sheet Wrap(Autodesk.Revit.DB.ViewSheet view, bool isRevitOwned)
        {
            return Sheet.FromExisting(view, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static DraftingView Wrap(Autodesk.Revit.DB.ViewDrafting view, bool isRevitOwned)
        {
            return DraftingView.FromExisting(view, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Topography Wrap(Autodesk.Revit.DB.Architecture.TopographySurface topoSurface, bool isRevitOwned)
        {
            return Topography.FromExisting(topoSurface, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Toposolid Wrap(Autodesk.Revit.DB.Toposolid toposolid, bool isRevitOwned)
        {
            return Toposolid.FromExisting(toposolid, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ToposolidType Wrap(Autodesk.Revit.DB.ToposolidType toposolidType, bool isRevitOwned)
        {
            return ToposolidType.FromExisting(toposolidType, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static object Wrap(Autodesk.Revit.DB.Panel ele, bool isRevitOwned)
        {
            if (AdaptiveComponentInstanceUtils.IsAdaptiveFamilySymbol(ele.Symbol))
            {
                return AdaptiveComponent.FromExisting(ele, isRevitOwned);
            }

            return CurtainPanel.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Mullion Wrap(Autodesk.Revit.DB.Mullion ele, bool isRevitOwned)
        {
            return Mullion.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Dimension Wrap(Autodesk.Revit.DB.Dimension ele, bool isRevitOwned)
        {
            return Dimension.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static DimensionType Wrap(Autodesk.Revit.DB.DimensionType ele, bool isRevitOwned)
        {
            return DimensionType.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static FilledRegionType Wrap(Autodesk.Revit.DB.FilledRegionType ele, bool isRevitOwned)
        {
            return FilledRegionType.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static FilledRegion Wrap(Autodesk.Revit.DB.FilledRegion ele, bool isRevitOwned)
        {
            return FilledRegion.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static FillPatternElement Wrap(Autodesk.Revit.DB.FillPatternElement ele, bool isRevitOwned)
        {
            return FillPatternElement.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static LinePatternElement Wrap(Autodesk.Revit.DB.LinePatternElement ele, bool isRevitOwned)
        {
            return LinePatternElement.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static TextNote Wrap(Autodesk.Revit.DB.TextNote ele, bool isRevitOwned)
        {
            return TextNote.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Tag Wrap(Autodesk.Revit.DB.IndependentTag ele, bool isRevitOwned)
        {
            return Tag.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static TextNoteType Wrap(Autodesk.Revit.DB.TextNoteType ele, bool isRevitOwned)
        {
            return TextNoteType.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Revision Wrap(Autodesk.Revit.DB.Revision ele, bool isRevitOwned)
        {
            return Revision.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static RevisionCloud Wrap(Autodesk.Revit.DB.RevisionCloud ele, bool isRevitOwned)
        {
            return RevisionCloud.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Revit.Filter.ParameterFilterElement Wrap(Autodesk.Revit.DB.ParameterFilterElement ele, bool isRevitOwned)
        {
            return Revit.Filter.ParameterFilterElement.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Room Wrap(Autodesk.Revit.DB.Architecture.Room ele, bool isRevitOwned)
        {
            return Room.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static DetailCurve Wrap(Autodesk.Revit.DB.DetailCurve ele, bool isRevitOwned)
        {
            return DetailCurve.FromExisting(ele, isRevitOwned);

        }

        [Obsolete("Please use ToDsType instead")]
        public static ImportInstance Wrap(Autodesk.Revit.DB.ImportInstance ele, bool isRevitOwned)
        {
            return ImportInstance.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static GlobalParameter Wrap(Autodesk.Revit.DB.GlobalParameter ele, bool isRevitOwned)
        {
            return GlobalParameter.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static FaceWall Wrap(Autodesk.Revit.DB.FaceWall ele, bool isRevitOwned)
        {
            return FaceWall.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static CurtainSystem Wrap(Autodesk.Revit.DB.CurtainSystem ele, bool isRevitOwned)
        {
            return CurtainSystem.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Material Wrap(Autodesk.Revit.DB.Material ele, bool isRevitOwned)
        {
            return Material.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static PathOfTravel Wrap(Autodesk.Revit.DB.Analysis.PathOfTravel ele, bool isRevitOwned)
        {
            return PathOfTravel.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Viewport Wrap(Autodesk.Revit.DB.Viewport ele, bool isRevitOwned)
        {
            return Viewport.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ElevationMarker Wrap(Autodesk.Revit.DB.ElevationMarker ele, bool isRevitOwned)
        {
            return ElevationMarker.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Space Wrap(Autodesk.Revit.DB.Mechanical.Space ele, bool isRevitOwned)
        {
            return Space.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static RoofType Wrap(Autodesk.Revit.DB.RoofType ele, bool isRevitOwned)
        {
            return RoofType.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static Roof Wrap(Autodesk.Revit.DB.RoofBase ele, bool isRevitOwned)
        {
            return Roof.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static ScheduleOnSheet Wrap(Autodesk.Revit.DB.ScheduleSheetInstance ele, bool isRevitOwned)
        {
            return ScheduleOnSheet.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static AppearanceAssetElement Wrap(Autodesk.Revit.DB.AppearanceAssetElement ele, bool isRevitOwned)
        {
            return AppearanceAssetElement.FromExisting(ele, isRevitOwned);
        }

        [Obsolete("Please use ToDsType instead")]
        public static LinkInstance Wrap(Autodesk.Revit.DB.RevitLinkInstance ele, bool isRevitOwned)
        {
            return LinkInstance.FromExisting(ele, isRevitOwned);
        }

        #endregion
    }
}
