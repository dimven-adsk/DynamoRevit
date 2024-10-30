using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Xml;
using Autodesk.DesignScript.Geometry;
using Autodesk.DesignScript.Interfaces;
using Autodesk.Revit.DB;
using Dynamo.Applications.Preview;
using Dynamo.Graph.Connectors;
using Dynamo.Graph.Nodes;
using Dynamo.Graph.Workspaces;
using Dynamo.Models;
using Dynamo.Visualization;
using Dynamo.Wpf.ViewModels.Watch3D;
using Dynamo.Wpf.Views.Debug;
using Revit.GeometryConversion;
using RevitServices.Persistence;
using RevitServices.Threading;
using RevitServices.Transactions;
using Curve = Autodesk.DesignScript.Geometry.Curve;
using Line = Autodesk.Revit.DB.Line;
using Point = Autodesk.DesignScript.Geometry.Point;
using Resources = Dynamo.Applications.Properties.Resources;
using Solid = Autodesk.DesignScript.Geometry.Solid;
using Surface = Autodesk.DesignScript.Geometry.Surface;

namespace Dynamo.Applications.ViewModel
{
    public class RevitWatch3DViewModel : DefaultWatch3DViewModel
    {
        private ElementId keeperId = ElementId.InvalidElementId;
        private ElementId directShapeId = ElementId.InvalidElementId;
        private MethodInfo method;

        public override string PreferenceWatchName { get { return "IsRevitBackgroundPreviewActive"; } }

        public RevitWatch3DViewModel(Watch3DViewModelStartupParams parameters) : base(null, parameters)
        {
            Name = Resources.BackgroundPreviewName;
            Draw();
        }

        protected override void OnShutdown()
        {
            DynamoRevitApp.AddIdleAction(DeleteKeeperElement);
        }

        protected override void OnClear()
        {
            IdlePromise.ExecuteOnIdleAsync(
                () =>
                {
                    TransactionManager.Instance.EnsureInTransaction(
                        DocumentManager.Instance.CurrentDBDocument);

                    if (keeperId != ElementId.InvalidElementId)
                    {
                        DocumentManager.Instance.CurrentUIDocument.Document.Delete(keeperId);
                        keeperId = ElementId.InvalidElementId;
                    }

                    TransactionManager.Instance.ForceCloseTransaction();
                });
        }

        public override bool Active
        {
            get => base.Active;
            set
            {
                if (active == value)
                {
                    return;
                }

                active = value;
                preferences.SetIsBackgroundPreviewActive(PreferenceWatchName, value);
                RaisePropertyChanged("Active");

                OnActiveStateChanged();
            }
        }

        protected override void OnActiveStateChanged()
        {
            if (active)
            {
                Draw();
            }
            else
            {
                OnClear();
            }
        }

        protected override void OnEvaluationCompleted(object sender, EvaluationCompletedEventArgs e)
        {
            Draw();
        }

        protected override void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            var node = sender as NodeModel;
            if (node == null)
            {
                return;
            }

            switch (e.PropertyName)
            {
                case "IsVisible":
                    Draw(node);
                    break;
            }
        }

        #region private methods

        Stopwatch _sw;
        private void Draw(NodeModel node = null)
        {
            // If there is no open Revit document, some nodes cannot be executed.
            if (!Active || DocumentManager.Instance.CurrentDBDocument == null) return;
            IEnumerable<IGraphicItem> graphicItems = new List<IGraphicItem>();

            _sw = Stopwatch.StartNew();

            if (node != null)
            {
                if (node.IsVisible)
                {
                    graphicItems = node.GeneratedGraphicItems(engineManager.EngineController);
                }
            }
            else
            {
                graphicItems = dynamoModel.CurrentWorkspace.Nodes
                 .Where(n => n.IsVisible)
                 .SelectMany(n => n.GeneratedGraphicItems(engineManager.EngineController));
            }

            var geoms = new List<GeometryObject>();
            foreach (var item in graphicItems)
            {
                RevitGeometryObjectFromGraphicItem(item, ref geoms);
            }

            Draw(geoms);
        }

        private void Draw(IEnumerable<GeometryObject> geoms)
        {
            if (method == null)
            {
                method = GetTransientDisplayMethod();

                if (method == null)
                    return;
            }

            IdlePromise.ExecuteOnIdleAsync(
                () =>
                {
                    TransactionManager.Instance.EnsureInTransaction(
                        DocumentManager.Instance.CurrentDBDocument);

                    if (keeperId != ElementId.InvalidElementId &&
                        DocumentManager.Instance.CurrentDBDocument.GetElement(keeperId) != null)
                    {
                        DocumentManager.Instance.CurrentUIDocument.Document.Delete(keeperId);
                        keeperId = ElementId.InvalidElementId;
                    }

                    var argsM = new object[4];
                    argsM[0] = DocumentManager.Instance.CurrentUIDocument.Document;
                    argsM[1] = ElementId.InvalidElementId;
                    argsM[2] = geoms;
                    argsM[3] = ElementId.InvalidElementId;
                    keeperId = (ElementId)method.Invoke(null, argsM);

                    TransactionManager.Instance.ForceCloseTransaction();
                    var elapsed = _sw.ElapsedMilliseconds;
                    Debug.WriteLine($"Transient element update took {elapsed}ms");
                });
        }

        private void RevitGeometryObjectFromGraphicItem(IGraphicItem item, ref List<GeometryObject> geoms)
        {
            var geom = item as PolyCurve;
            if (geom != null)
            {
                // We extract the curves explicitly rather than using PolyCurve's ToRevitType
                // extension method.  There is a potential issue with CurveLoop which causes
                // this method to introduce corrupt GNodes.  
                foreach (var c in geom.Curves())
                {
                    // Tesselate the curve.  This greatly improves performance when
                    // we're dealing with NurbsCurve's with high knot count, commonly
                    // results of surf-surf intersections.
                    geoms.AddRange(Tessellate(c));
                }
                        
                return;
            }

            var point = item as Point;
            if (point != null)
            {
                Autodesk.Revit.DB.Point pnt = null;
                try
                {
                    pnt = Autodesk.Revit.DB.Point.Create(point.ToXyz());
                }
                catch(Exception)
                {
                }
                finally
                {
                    if (pnt != null)
                    {
                        geoms.Add(pnt);
                    }
                }
                return;
            }

            var curve = item as Curve;
            if (curve != null)
            {
                // Tesselate the curve.  This greatly improves performance when
                // we're dealing with NurbsCurve's with high knot count, commonly
                // results of surf-surf intersections.
                geoms.AddRange(Tessellate(curve));
                return;
            }

            var surf = item as Surface;
            if (surf != null)
            {
                geoms.AddRange(Tessellate(surf));
                return;
            }

            var solid = item as Solid;
            if (solid != null)
            {
                geoms.AddRange(Tessellate(solid));
            }
        }

        /// <summary>
        /// Tessellate the curve:
        /// 1). If there are more than 2 points, create a polyline out of the points;
        /// 2). If there are exactly 2 points, create a line;
        /// 3). If there's exception thrown during the tessellation process, attempt to create 
        /// a line from start and end points. If that fails, a point will be created instead.
        /// </summary>
        /// <param name="curve"></param>
        /// <returns></returns>
        private IEnumerable<GeometryObject> Tessellate(Curve curve)
        {
            var result = new List<GeometryObject>();
            try
            {
                // we scale the tesselation rather than the curve
                var conv = UnitConverter.DynamoToHostFactor(SpecTypeId.Length);

                // use the ASM tesselation of the curve
                var pkg = renderPackageFactory.CreateRenderPackage();
                curve.Tessellate(pkg, renderPackageFactory.TessellationParameters);

                // get necessary info to enumerate and convert the lines
                //var lineCount = pkg.LineVertexCount * 3 - 3;
                var verts = pkg.LineStripVertices.ToList();

                if (verts.Count > 2)
                {
                    var scaledXYZs = new List<XYZ>();
                    for (var i = 0; i < verts.Count; i += 3)
                    {
                        scaledXYZs.Add(new XYZ(verts[i] * conv, verts[i + 1] * conv, verts[i + 2] * conv));
                    }
                    result.Add(PolyLine.Create(scaledXYZs));
                }
                else if (verts.Count == 2)
                {
                    result.Add(Line.CreateBound(curve.StartPoint.ToXyz(), curve.EndPoint.ToXyz()));
                }
            }
            catch (Exception)
            {
                // Add a red bounding box geometry to identify that some errors occur
                var bbox = curve.BoundingBox;
                result.AddRange(ProtoToRevitMesh.CreateBoundingBoxMeshForErrors(bbox.MinPoint, bbox.MaxPoint));

                try
                {
                    result.Add(Line.CreateBound(curve.StartPoint.ToXyz(), curve.EndPoint.ToXyz()));
                }
                catch (Exception)
                {
                    try
                    {
                        result.Add(Autodesk.Revit.DB.Point.Create(curve.StartPoint.ToXyz()));
                    }
                    catch (ArgumentException)
                    {
                        //if either the X, Y or Z of the point is infinite, no need to add it for preview
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Tessellate the surface by calling the ToRevitType function.
        /// If it fails, each edge of the surface will be tessellated instead by calling
        /// the correspoinding Tessellate method.
        /// </summary>
        /// <param name="surface"></param>
        /// <returns></returns>
        private List<GeometryObject> Tessellate(Surface surface)
        {
            List<GeometryObject> rtGeoms = new List<GeometryObject>();

            try
            {
                rtGeoms.AddRange(surface.ToRevitType());
            }
            catch (Exception)
            {
                // Add a red bounding box geometry to identify that some errors occur
                var bbox = surface.BoundingBox;
                rtGeoms.AddRange(ProtoToRevitMesh.CreateBoundingBoxMeshForErrors(bbox.MinPoint, bbox.MaxPoint));

                foreach (var edge in surface.Edges)
                {
                    if (edge != null)
                    {
                        var curveGeometry = edge.CurveGeometry;
                        rtGeoms.AddRange(Tessellate(curveGeometry));
                        curveGeometry.Dispose();
                        edge.Dispose();
                    }
                }
            }

            return rtGeoms;
        }

        /// <summary>
        /// Tessellate the solid by calling the ToRevitType function.
        /// If it fails, the surface geometry of each face of the solid will be tessellated
        /// instead by calling the correspoinding Tessellate method.
        /// </summary>
        /// <param name="solid"></param>
        /// <returns></returns>
        private List<GeometryObject> Tessellate(Solid solid)
        {
            var pkg = renderPackageFactory.CreateRenderPackage();
            solid.Tessellate(pkg, renderPackageFactory.TessellationParameters);
            List<GeometryObject> rtGeoms = new List<GeometryObject>();

            try
            {
                rtGeoms.AddRange(solid.ToRevitType());
            }
            catch(Exception)
            {
                // Add a red bounding box geometry to identify that some errors occur
                var bbox = solid.BoundingBox;
                rtGeoms.AddRange(ProtoToRevitMesh.CreateBoundingBoxMeshForErrors(bbox.MinPoint, bbox.MaxPoint));

                foreach (var face in solid.Faces)
                {
                    if (face != null)
                    {
                        var surfaceGeometry = face.SurfaceGeometry();
                        rtGeoms.AddRange(Tessellate(surfaceGeometry));
                        surfaceGeometry.Dispose();
                        face.Dispose();
                    }
                }
            }

            return rtGeoms;
        }

        /// <summary>
        /// This method access Revit API, therefore it needs to be called only 
        /// by idle thread (i.e. in an 'UIApplication.Idling' event handler).
        /// </summary>
        private void DeleteKeeperElement()
        {
            // Only try to delete the keeper element when we have been initialized (e.g. we have a valid keeperId).
            // Check for this condition before trying to access the current Revit document because there
            // are cases when we get here uninitialized with a document that is gone already. 
            if (keeperId == ElementId.InvalidElementId)
            {
                return;
            }
   
            // Never access the current document with an invalid keeperId
            // See comment at the beginning of this method.
            var dbDoc = DocumentManager.Instance.CurrentUIApplication.ActiveUIDocument.Document;
            if (null == dbDoc)
            {
                return;
            }

            TransactionManager.Instance.EnsureInTransaction(dbDoc);
            dbDoc.Delete(keeperId);
            TransactionManager.Instance.ForceCloseTransaction();
        }

        internal static MethodInfo GetTransientDisplayMethod()
        {
            var geometryElementType = typeof(GeometryElement);
            var geometryElementTypeMethods =
                geometryElementType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            var method = geometryElementTypeMethods.FirstOrDefault(x => x.Name == "SetForTransientDisplay");

            return method;
        }
        #endregion
    }

    internal class NodeState
    {
        public required Guid Guid { get; set; }
        public bool Selected { get; set; } = false;
        public bool Visible { get; set; }
        public bool Dirty { get; set; } = true;
        public bool GeometryUpdated { get; set; } = false;

        public void ClearFlags()
        {
            Dirty = false;
            GeometryUpdated = false;
        }

        public void UpdateState(NodeModel node)
        {
            if (node.IsSelected != Selected)
            {
                Dirty = true;
                Selected = node.IsSelected;
            }
            if (node.IsVisible != Visible)
            {
                Dirty = true;
                Visible = node.IsVisible;
            }
        }

        public void UpdateGeometry()
        {
            GeometryUpdated = true;
            Dirty = true;
        }
    }

    public class RevitDirectContextWatch3DViewModel : DefaultWatch3DViewModel
    {
        private class NodeGraphicsDebouncer
        {
            Dispatcher _dispatcher;
            CancellationTokenSource _cts;
            public NodeGraphicsDebouncer(Dispatcher dispatcher)
            {
                _dispatcher = dispatcher;
            }

            public void Debounce(int timeoutMillis, Action action)
            {
                _cts?.Cancel();
                if (timeoutMillis <= 0)
                {
                    _cts = null;
                    _dispatcher.Invoke(action, DispatcherPriority.Background);
                    return;
                }
                _cts = new CancellationTokenSource();

                Task.Delay(timeoutMillis, _cts.Token).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        _dispatcher.Invoke(action, DispatcherPriority.Background);
                });
            }
        }

        private NodeGraphicsDebouncer _debouncer;
        private PreviewServer _server;
        private BufferPools _pools;
        private Dictionary<Guid, NodeState> _nodeStates = new Dictionary<Guid, NodeState>();

        public override string PreferenceWatchName { get { return "IsRevitBackgroundPreviewActive"; } }

        public RevitDirectContextWatch3DViewModel(Watch3DViewModelStartupParams parameters) : base(null, parameters)
        {
            Name = Resources.BackgroundPreviewName;
            _debouncer = new NodeGraphicsDebouncer(Dispatcher.CurrentDispatcher);
            //Draw(redrawAll: true);
        }

        protected override void OnShutdown()
        {
            DynamoRevitApp.AddIdleAction(StopServer);
        }

        private void StopServer()
        {
            _server?.StopServer();
            _server = null;
            DocumentManager.Instance.CurrentUIDocument.RefreshActiveView();
        }

        protected override void OnClear()
        {
            IdlePromise.ExecuteOnIdleAsync(StopServer);
        }

        public override bool Active
        {
            get => base.Active;
            set
            {
                if (active == value)
                    return;

                active = value;
                preferences.SetIsBackgroundPreviewActive(PreferenceWatchName, value);
                RaisePropertyChanged(nameof(Active));

                OnActiveStateChanged();
            }
        }

        protected override void OnActiveStateChanged()
        {
            if (active)
            {
                //this.del
                Debug.WriteLine("OnActiveStateChanged");
                var nodes = AllNodes().ToList();
                nodes.ForEach(n => GetNodeState(n.GUID).UpdateGeometry());
                ScheduleRedraw(0);
            }
            else
            {
                OnClear();
            }
        }

        private void ScheduleRedraw(int timeoutMillis)
        {
            if (_debouncer == null)
                _debouncer = new NodeGraphicsDebouncer(Dispatcher.CurrentDispatcher);

            _debouncer.Debounce(timeoutMillis, () =>
            {
                Debug.WriteLine("Successful debounce!");
                var nodes = AllNodes().Where(n => _nodeStates.TryGetValue(n.GUID, out var state) && state.Dirty).ToList();
                Draw(nodes, null, true);
            });
        }

        private NodeState GetNodeState(Guid nodeGuid)
        {
            NodeState state;
            if (_nodeStates.TryGetValue(nodeGuid, out state))
                return state;
            state = _nodeStates[nodeGuid] = new NodeState { Guid = nodeGuid };
            return state;
        }

        private IEnumerable<NodeModel> AllNodes()
        {
            return dynamoModel.CurrentWorkspace.Nodes;
        }

        private Dictionary<Guid, NodeModel> GetRelevantNodes(IEnumerable<NodeModel> raw, IEnumerable<Guid> guids = null)
        {
            if (guids == null)
                return raw.ToDictionary(n => n.GUID);

            var nodes = guids.ToDictionary<Guid, Guid, NodeModel>(g => g, g => null);
            foreach(var node in raw)
            {
                if (nodes.ContainsKey(node.GUID))
                    nodes[node.GUID] = node;
            }
            return nodes;
        }

        protected override void OnEvaluationCompleted(object sender, EvaluationCompletedEventArgs e)
        {
            Debug.WriteLine($"Evaluation completed, redrawing changed nodes");
            var nodes = AllNodes().Where(n => n.WasInvolvedInExecution).ToList();
            nodes.ForEach(n => GetNodeState(n.GUID).UpdateGeometry());
            ScheduleRedraw(0);
            //Draw(nodes, null, true);
        }

        protected override void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is not NodeModel node)
                return;

            if (e.PropertyName == nameof(node.IsVisible) || e.PropertyName == nameof(node.IsSelected))
            {
                Debug.WriteLine($"IsVisible or IsSelected changed for node {node.GUID}");
                GetNodeState(node.GUID).UpdateState(node);
                ScheduleRedraw(50);

                //Draw([node], null, false);
            }
        }

        public override void RemoveGeometryForNode(NodeModel node)
        {
            Debug.WriteLine($"Redrawing geometry for node {node.GUID}");
            GetNodeState(node.GUID).UpdateGeometry();
            ScheduleRedraw(50);
            //var nodeGraphics = UpdateNodeState(node, true);
            //Draw([nodeGraphics], []);
            //Draw([node], null, true);
        }

        public override void DeleteGeometryForNode(NodeModel node, bool requestUpdate = true)
        {
            var guid = node.GUID;
            Debug.WriteLine($"Deleting geometry for node {guid}");
            if (_nodeStates.TryGetValue(guid, out var nodeState))
            {
                _nodeStates.Remove(guid);
                var downStreamNodes = new HashSet<NodeModel>();
                node.GetDownstreamNodes(node, downStreamNodes);
                downStreamNodes.Remove(node);
                Draw(downStreamNodes, [guid], true);
            }
        }

        #region private methods
        private class NodeGraphicItems
        {
            public NodeState State { get; set; }
            public List<IGraphicItem> GraphicItems { get; set; }
        }

        private NodeGraphicItems UpdateNodeState(NodeModel node, bool rebuildGeometry)
        {
            var guid = node.GUID;

            NodeState state;
            if (!_nodeStates.TryGetValue(guid, out state))
                state = _nodeStates[guid] = new NodeState { Guid = guid };

            state.Dirty = true;
            state.UpdateState(node);
            List<IGraphicItem> items = null;
            if (rebuildGeometry)
            {
                state.GeometryUpdated = true;
                items = node.GeneratedGraphicItems(engineManager.EngineController);
            }

            return new NodeGraphicItems { State = state, GraphicItems = items };
        }

        private void Draw(IEnumerable<NodeModel> nodes, IEnumerable<Guid> deleted, bool rebuildGeometry)
        {
            if (!Active || DocumentManager.Instance.CurrentDBDocument == null) return;

            //var dirty = new HashSet<Guid>(dirtyNodeGuids ?? []);
            var graphicItems = new List<NodeGraphicItems>();
            //var deletedNodeGuids = _nodeStates.Keys.ToHashSet();
            foreach(var node in nodes)
            {
                var guid = node.GUID;
                //deletedNodeGuids.Remove(guid);

                NodeState state;
                if (!_nodeStates.TryGetValue(guid, out state))
                    state = _nodeStates[guid] = new NodeState { Guid = guid };

                state.Dirty = true;
                state.UpdateState(node);
                List<IGraphicItem> items = null;
                if (state.GeometryUpdated)
                {
                    state.GeometryUpdated = true;
                    items = node.GeneratedGraphicItems(engineManager.EngineController);
                }

                //if (state.Dirty)
                graphicItems.Add(new NodeGraphicItems { State = state, GraphicItems = items });
            }

            if (graphicItems.Count == 0 && deleted == null)
                return;

            Draw(graphicItems, deleted ?? Enumerable.Empty<Guid>());
        }

        IRenderPackage _pkg;
        private void UpdateCached(NodePreviewObject cached, NodeGraphicItems node, IRenderPackage _pkg)
        {
            cached.Selected = node.State.Selected;
            cached.Visible = node.State.Visible;
            node.State.ClearFlags();
            if (node.GraphicItems == null || node.GraphicItems.Count == 0)
                return;

            cached.Clear();
            foreach(var item in node.GraphicItems)
            {
                _pkg.Clear();
                switch (item) {
                    //case Point point:
                    //    var scale = UnitConverter.DynamoToHostFactor(SpecTypeId.Length);
                    //    var transform = Transform.CreateTranslation(point.ToXyz() * scale);
                    //    //GetLazyPreviewBox().AddTransform(transform);
                    //    // GetPreviewBox().AddTransform(transform);
                    //    break;
                    //case PolyCurve polyCurve:
                    //    polyCurve.Tessellate(_pkg, renderPackageFactory.TessellationParameters);
                    //    break;
                    //case Curve curve:
                    //    curve.Tessellate(_pkg, renderPackageFactory.TessellationParameters);
                    //    break;
                    case Surface surface:
                        //pkg = renderPackageFactory.CreateRenderPackage();
                        surface.Tessellate(_pkg, renderPackageFactory.TessellationParameters);
                        cached.AddMesh(_pkg);
                        break;
                    case Solid solid:
                        //pkg = renderPackageFactory.CreateRenderPackage();
                        solid.Tessellate(_pkg, renderPackageFactory.TessellationParameters);
                        cached.AddMesh(_pkg);
                        break;
                    default:
                        break;
                }
            }
        }

        private void Draw(IEnumerable<NodeGraphicItems> nodes, IEnumerable<Guid> deletedNodes)
        {
            IdlePromise.ExecuteOnIdleAsync(
                () =>
                {
                    if (_server == null)
                        _server = PreviewServer.StartNewServer(_pools);

                    if (_pkg == null)
                        _pkg = renderPackageFactory.CreateRenderPackage();

                    foreach(var nodeGuid in deletedNodes)
                        _server.DeletePreview(nodeGuid);

                    var sw = Stopwatch.StartNew();
                    var nodeElapsed = sw.ElapsedMilliseconds;
                    foreach(var node in nodes)
                    {
                        var wasDirty = node.State.Dirty;
                        _server.WithNodeCache(node.State.Guid, cached => UpdateCached(cached, node, _pkg));
                        var current = sw.ElapsedMilliseconds;
                        Debug.WriteLine($"Graphics update for node {node.State.Guid} took {current - nodeElapsed}ms, dirty? {wasDirty}");
                        nodeElapsed = current;
                    }

                    var elapsed = sw.ElapsedMilliseconds;
                    Debug.WriteLine($"Spent {elapsed}ms building the preview geometry");
                    DocumentManager.Instance.CurrentUIDocument.RefreshActiveView();
                    elapsed = sw.ElapsedMilliseconds - elapsed;
                    Debug.WriteLine($"Spent {elapsed}ms refreshing the active view");
                }
            );
        }

        private void DebugMe([CallerMemberName] string callerName = null)
        {
            Debug.WriteLine($"Debugging from inside from {callerName}");
        }

        protected override void OnIsolationModeRequestUpdate()
        {
            // TODO dont override
            DebugMe();
            base.OnIsolationModeRequestUpdate();
        }

        protected override void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            DebugMe();
            base.OnModelPropertyChanged(sender, e);
        }

        protected override void OnWorkspaceCleared(WorkspaceModel workspace)
        {
            // TODO dont override
            DebugMe();
            base.OnWorkspaceCleared(workspace);
        }

        protected override void OnWorkspaceOpening(object obj)
        {
            // TODO dont override
            DebugMe();
            base.OnWorkspaceOpening(obj);
        }

        protected override void OnWorkspaceSaving(XmlDocument doc)
        {
            DebugMe();
            base.OnWorkspaceSaving(doc);
        }

        protected override void PortConnectedHandler(PortModel arg1, ConnectorModel arg2)
        {
            DebugMe();
            base.PortConnectedHandler(arg1, arg2);
        }

        protected override void PortDisconnectedHandler(PortModel port)
        {
            DebugMe();
            base.PortDisconnectedHandler(port);
        }

        protected override void SelectionChangedHandler(object sender, NotifyCollectionChangedEventArgs e)
        {
            // TODO dont override - handled by node selection state
            DebugMe();
            base.SelectionChangedHandler(sender, e);
        }

        Stopwatch _watch;
        TimeSpan _last;
        protected override void OnRenderPackagesUpdated(NodeModel node, RenderPackageCache packages)
        {
            // TODO dont override - handled by RemoveGeometryForNode itself
            DebugMe();
            _watch ??= Stopwatch.StartNew();
            var span = _watch.Elapsed;
            if (_last == TimeSpan.Zero)
                Debug.WriteLine($"Hello {span}");
            else
                Debug.WriteLine($"Hello {span - _last}");
            _last = span;
            return;

            var pkg = packages.Packages.FirstOrDefault();
            if (pkg != null)
            {
                if (pkg.MeshVertexCount > 0 || pkg.LineVertexCount > 0)
                {
                    return;
                    IdlePromise.ExecuteOnIdleAsync(
                        () =>
                        {
                            if (_server == null)
                                _server = PreviewServer.StartNewServer(_pools);

                            var sw = Stopwatch.StartNew();
                            _server.WithNodeCache(node.GUID, cached =>
                            {
                                cached.Clear();
                                cached.Visible = true;
                                if (pkg.MeshVertexCount > 0)
                                    cached.AddMesh(pkg);
                                if (pkg.LineVertexCount > 0)
                                    cached.AddEdge(pkg);
                            });
                            var elapsed = sw.ElapsedMilliseconds;
                            Debug.WriteLine($"Graphics update for node {node.GUID} took {elapsed}ms");
                            DocumentManager.Instance.CurrentUIDocument.RefreshActiveView();
                            elapsed = sw.ElapsedMilliseconds - elapsed;
                            Debug.WriteLine($"Active view refresh took {elapsed}ms");
                        }
                    );
                }
            }
            base.OnRenderPackagesUpdated(node, packages);
        }
        #endregion
    }
}
