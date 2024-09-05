using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.DesignScript.Runtime;
using RevitServices.Persistence;
using DB = Autodesk.Revit.DB;

namespace Revit.Elements.InternalUtilities
{
    [IsVisibleInDynamoLibrary(false)]
    public static class ElementQueries
    {
        internal static readonly HashSet<Type> ClassFilterExceptions = new HashSet<Type>
        {
            typeof(DB.HostedSweep),
            typeof(DB.Architecture.Room),
            typeof(DB.Mechanical.Space),
            typeof(DB.Area),
            typeof(DB.Architecture.RoomTag),
            typeof(DB.Mechanical.SpaceTag),
            typeof(DB.AreaTag),
            typeof(DB.Mullion),
            typeof(DB.Panel),
            typeof(DB.AnnotationSymbol),
            typeof(DB.Structure.AreaReinforcementType),
            typeof(DB.Structure.PathReinforcementType),
            typeof(DB.AnnotationSymbolType),
            typeof(DB.Architecture.RoomTagType),
            typeof(DB.Mechanical.SpaceTagType),
            typeof(DB.AreaTagType),
            typeof(DB.Structure.TrussType),
            typeof(DB.Structure.AreaReinforcementCurve),
            typeof(DB.CurveByPoints),
            typeof(DB.DetailArc),
            typeof(DB.DetailCurve),
            typeof(DB.DetailEllipse),
            typeof(DB.DetailLine),
            typeof(DB.DetailNurbSpline),
            typeof(DB.Architecture.Fascia),
            typeof(DB.Architecture.Gutter),
            typeof(DB.ModelArc),
            typeof(DB.ModelCurve),
            typeof(DB.ModelEllipse),
            typeof(DB.ModelHermiteSpline),
            typeof(DB.ModelLine),
            typeof(DB.ModelNurbSpline),
            typeof(DB.SlabEdge),
            typeof(DB.SymbolicCurve)
        };

        internal static Type GetClassFilterExceptionsValidType(Type elementType)
        {
            if (ClassFilterExceptions.Contains(elementType.BaseType))
            {
                return GetClassFilterExceptionsValidType(elementType.BaseType);
            }
            else
            {
                return elementType.BaseType;
            }
        }

        public static IList<Element> OfFamilyType(FamilyType familyType)
        {
            if (familyType == null) return null;

            var doc = DocumentManager.Instance.CurrentDBDocument;
            var instanceFilter = new DB.ElementClassFilter(typeof(DB.FamilyInstance));
            var typeFilter = new DB.FamilyInstanceFilter(doc, familyType.InternalElement.Id);
            var fec = new DB.FilteredElementCollector(doc);

            return fec.WhereElementIsNotElementType()
                .WherePasses(instanceFilter)
                .WherePasses(typeFilter)
                .Select(e => e.ToDSType(true))
                .ToList();
        }

        public static IList<Element> OfElementType(Type elementType)
        {
            if (elementType == null) return null;

            /*
            (Konrad) According to RevitAPI documentation the quick filter
            ElementClassFilter() has certain limitations that prevent it
            from working on certain derived classes. In that case we need
            to collect elements from base class and then perform additional
            filtering to get our intended element set.
            */

            if (ClassFilterExceptions.Contains(elementType))
            {
                var type = GetClassFilterExceptionsValidType(elementType);
                return new DB.FilteredElementCollector(DocumentManager.Instance.CurrentDBDocument)
                    .OfClass(type)
                    .Where(x => x.GetType() == elementType)
                    .Select(e => e.ToDSType(true))
                    .ToList();
            }

            var classFilter = new DB.ElementClassFilter(elementType);
            return new DB.FilteredElementCollector(DocumentManager.Instance.CurrentDBDocument)
                .WherePasses(classFilter)
                .Select(e => e.ToDSType(true))
                .ToList();
        }

        public static IList<Element> OfCategory(Category category, Revit.Elements.Views.View view = null)
        {
            if (category == null) return null;

            var catFilter = new DB.ElementCategoryFilter(category.InternalCategory.Id);
            var fec = (view == null) ?
                new DB.FilteredElementCollector(DocumentManager.Instance.CurrentDBDocument) :
                new DB.FilteredElementCollector(DocumentManager.Instance.CurrentDBDocument, view.InternalView.Id);
            var instances =
                fec.WherePasses(catFilter)
                    .WhereElementIsNotElementType()
                    .Select(e => e.ToDSType(true))
                    .ToList();
            return instances;
        }

        public static IList<Element> AtLevel(Level arg)
        {
            if (arg == null) return null;

            var levFilter = new DB.ElementLevelFilter(arg.InternalLevel.Id);
            var fec = new DB.FilteredElementCollector(DocumentManager.Instance.CurrentDBDocument);
            var instances =
                fec.WherePasses(levFilter)
                    .WhereElementIsNotElementType()
                    .Select(e => e.ToDSType(true))
                    .ToList();
            return instances;
        }

        public static Element ById(object id)
        {
            if (id == null)
                return null;

            // handle ElementId types first
            if (id.GetType() == typeof(DB.ElementId))
                return ElementSelector.ByElementId(((DB.ElementId)id).Value);

            var idType = Type.GetTypeCode(id.GetType());
            Element element;

            switch (idType)
            {
                case TypeCode.Int64:
                    element = ElementSelector.ByElementId((long)id);
                    break;

                case TypeCode.Int32:
                    element = ElementSelector.ByElementId((int)id);
                    break;

                case TypeCode.String:
                    string idString = (string)id;
                    long intId;
                    if (Int64.TryParse(idString, out intId))
                    {
                        element = ElementSelector.ByElementId(intId);
                        break;
                    }

                    element = ElementSelector.ByUniqueId(idString);
                    break;

                default:
                    throw new InvalidOperationException(Revit.Properties.Resources.InvalidElementId);
            }

            return element;
        }

        internal static IEnumerable<DB.Level> GetAllLevels()
        {
            var collector = new DB.FilteredElementCollector(DocumentManager.Instance.CurrentDBDocument);
            collector.OfClass(typeof(DB.Level));
            return collector.Cast<DB.Level>();
        }

        public static List<List<Element>> RoomsByStatus()
        {
            var roomsByStatus = new List<List<Element>>();
            DB.Document doc = DocumentManager.Instance.CurrentDBDocument;
            var allRooms = new DB.FilteredElementCollector(doc)
                .OfCategory(DB.BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<DB.Architecture.Room>();

            var placedRooms = new List<Revit.Elements.Element>();
            var unplacedRooms = new List<Revit.Elements.Element>();
            var notEnclosedRooms = new List<Revit.Elements.Element>();
            var redundantRooms = new List<Revit.Elements.Element>();

            var opt = new DB.SpatialElementBoundaryOptions();

            foreach (DB.Architecture.Room room in allRooms)
            {
                var dsRoom = room.ToDSType(true);
                if (room.Area > 0)
                {
                    placedRooms.Add(dsRoom);
                    continue;
                }

                if (room.Location == null)
                {
                    unplacedRooms.Add(dsRoom);
                    continue;
                }
                if (room.GetBoundarySegments(opt) == null || (room.GetBoundarySegments(opt)).Count == 0)
                {
                    notEnclosedRooms.Add(dsRoom);
                    continue;
                }

                redundantRooms.Add(dsRoom);
            }

            roomsByStatus.Add(placedRooms);
            roomsByStatus.Add(unplacedRooms);
            roomsByStatus.Add(notEnclosedRooms);
            roomsByStatus.Add(redundantRooms);

            return roomsByStatus;
        }
    }
}
