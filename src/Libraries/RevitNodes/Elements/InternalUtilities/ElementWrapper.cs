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
    }

}
