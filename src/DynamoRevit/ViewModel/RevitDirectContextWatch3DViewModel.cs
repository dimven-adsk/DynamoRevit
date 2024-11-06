using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Autodesk.DesignScript.Interfaces;
using Dynamo.Applications.Preview;
using Dynamo.Graph.Nodes;
using Dynamo.Models;
using Dynamo.Visualization;
using Dynamo.Wpf.ViewModels.Watch3D;
using RevitServices.Persistence;
using RevitServices.Threading;
using Resources = Dynamo.Applications.Properties.Resources;

namespace Dynamo.Applications.ViewModel
{
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
                _cts?.Dispose();
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
        private Dictionary<Guid, IRenderPackage> _inactiveNodeGeometry = new Dictionary<Guid, IRenderPackage>();

        public override string PreferenceWatchName { get { return "IsRevitBackgroundPreviewActive"; } }

        public RevitDirectContextWatch3DViewModel(Watch3DViewModelStartupParams parameters) : base(null, parameters)
        {
            Name = Resources.BackgroundPreviewName;
            _debouncer = new NodeGraphicsDebouncer(Dispatcher.CurrentDispatcher);
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
            _nodeStates.Clear();
            _inactiveNodeGeometry.Clear();
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

        public bool CanDraw => Active && DocumentManager.Instance.CurrentDBDocument != null;

        protected override void OnActiveStateChanged()
        {
            if (active)
            {
                Debug.WriteLine("OnActiveStateChanged");
                var nodesToUpdate = new List<NodeRenderPackage>();
                foreach(var node in dynamoModel.CurrentWorkspace.Nodes)
                {
                    var guid = node.GUID;
                    if (_inactiveNodeGeometry.TryGetValue(guid, out var cached) && cached != null)
                    {
                        var state = GetOrCreateNodeState(guid);
                        state.UpdateGeometry();

                        nodesToUpdate.Add(new NodeRenderPackage { State = state, RenderPackage = cached });
                    }
                }
                _inactiveNodeGeometry.Clear();

                if (nodesToUpdate.Count == 0)
                    return;

                ExecuteOnPreviewServer(server =>
                {
                    foreach(var node in nodesToUpdate)
                        server.WithNodeCache(node.State.Guid, cached => UpdateCached(cached, node));
                    ScheduleRefresh(0);
                });
            }
            else
            {
                OnClear();
            }
        }

        private void ScheduleRefresh(int timeoutMillis)
        {
            if (_debouncer == null)
                _debouncer = new NodeGraphicsDebouncer(Dispatcher.CurrentDispatcher);

            _debouncer.Debounce(timeoutMillis, () =>
            {
                Debug.WriteLine("Refresh active view in debouncer");
                DocumentManager.Instance.CurrentUIDocument.RefreshActiveView();
            });
        }

        private NodeState GetOrCreateNodeState(Guid nodeGuid)
        {
            NodeState state;
            if (_nodeStates.TryGetValue(nodeGuid, out state))
                return state;
            state = _nodeStates[nodeGuid] = new NodeState { Guid = nodeGuid };
            return state;
        }

        protected override void OnEvaluationCompleted(object sender, EvaluationCompletedEventArgs e)
        {
            DebugMe();
        }

        protected override void OnNodePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (sender is not NodeModel node)
                return;

            if (e.PropertyName == nameof(node.IsVisible) || e.PropertyName == nameof(node.IsSelected))
            {
                var guid = node.GUID;
                Debug.WriteLine($"IsVisible or IsSelected changed for node {guid}");
                var state = GetOrCreateNodeState(guid).UpdateState(node);
                UpdateNode(guid, null);
            }
        }

        public override void RemoveGeometryForNode(NodeModel node)
        {
            DebugMe();
        }

        public override void DeleteGeometryForNode(NodeModel node, bool requestUpdate = true)
        {
            var guid = node.GUID;
            Debug.WriteLine($"Deleting geometry for node {guid}");
            if (_nodeStates.TryGetValue(guid, out var nodeState))
            {
                _nodeStates.Remove(guid);
                _inactiveNodeGeometry.Remove(guid);
                ExecuteOnPreviewServer(server => {
                    _server.DeletePreview(guid);
                    ScheduleRefresh(50);
                });
            }
        }

        #region private methods
        private class NodeRenderPackage
        {
            public NodeState State { get; set; }
            public IRenderPackage RenderPackage { get; set; }
        }

        private void ExecuteOnPreviewServer(Action<PreviewServer> action)
        {
            IdlePromise.ExecuteOnIdleAsync(() =>
            {
                Debug.WriteLine($"Executing action on preview server");
                var sw = Stopwatch.StartNew();
                if (_server == null)
                    _server = PreviewServer.StartNewServer(_pools);

                action(_server);
            });
        }

        private void DebugMe([CallerMemberName] string callerName = null)
        {
            Debug.WriteLine($"Debug from {callerName}");
        }

        protected override void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            DebugMe();
        }

        protected override void OnWorkspaceOpening(object obj)
        {
            // TODO dont override
            DebugMe();
        }

        public override void DeleteGeometryForIdentifier(string identifier, bool requestUpdate = true)
        {
            // PortDisconnectedHandler calls this
            // this could let us remove stale geometry for a node, which is what the Helix preview does
            DebugMe();
        }

        private void UpdateCached(NodePreviewObject cached, NodeRenderPackage node)
        {
            cached.Selected = node.State.Selected;
            cached.Visible = node.State.Visible;
            node.State.ClearFlags();
            var pkg = node.RenderPackage;
            if (pkg == null)
                return;

            cached.Clear();
            if (pkg.MeshVertexCount > 0)
                cached.AddMesh(pkg);
            if (pkg.LineVertexCount > 0)
                cached.AddEdge(pkg);
        }

        private void UpdateNode(Guid nodeGuid, IRenderPackage renderPackage)
        {
            var state = GetOrCreateNodeState(nodeGuid);
            var geometry = new NodeRenderPackage { State = state, RenderPackage = renderPackage };
            Debug.WriteLine($"Enqueuing preview server action, waiting for package to finish building");
            ExecuteOnPreviewServer(server =>
            {
                Debug.WriteLine($"Starting preview server update");
                var sw = Stopwatch.StartNew();
                server.WithNodeCache(state.Guid, cached => UpdateCached(cached, geometry));

                var elapsed = sw.ElapsedMilliseconds;
                Debug.WriteLine($"Graphics update for node {state.Guid} took {elapsed}ms");
                ScheduleRefresh(50);
            });
        }

        //Stopwatch _watch;
        //TimeSpan _last;
        protected override void OnRenderPackagesUpdated(NodeModel node, RenderPackageCache packages)
        {
            var sw = Stopwatch.StartNew();
            DebugMe();

            var guid = node.GUID;
            // this assumes the first package is a HelixRenderPackage, which might be a bad
            // assumption, but it enables us to piggyback off of the work that the Helix
            // render package has already done, essentially making the Revit preview free 
            var pkg = packages.Packages.FirstOrDefault(); 
            if (pkg != null && (pkg.MeshVertexCount > 0 || pkg.LineVertexCount > 0))
            {
                // cache the package so that we have something to draw
                // once the server is reactivated (if ever)
                // NOTE: this might use a large amount of memory
                _inactiveNodeGeometry[guid] = pkg;
                if (!CanDraw)
                {
                    Debug.WriteLine($"OnRenderPackagesUpdated saved inactive geometry in {sw.Elapsed}");
                    return;
                }

                UpdateNode(guid, pkg);
                Debug.WriteLine($"OnRenderPackagesUpdated enqueued in {sw.Elapsed}");
            }
            else
            {
                // remove old geometry if the renderpackage is empty, which prevents keeping
                // stale geometry while reducing the memory usage
                _inactiveNodeGeometry.Remove(guid);
                Debug.WriteLine($"OnRenderPackagesUpdated did nothing in {sw.Elapsed}");
            }
            //base.OnRenderPackagesUpdated(node, packages);
        }
        #endregion
    }

    internal class NodeState
    {
        public required Guid Guid { get; init; }
        public bool Selected { get; private set; } = false;
        public bool Visible { get; private set; } = true;
        public bool Dirty { get; private set; } = true;
        public bool GeometryUpdated { get; private set; } = false;

        public void ClearFlags()
        {
            Dirty = false;
            GeometryUpdated = false;
        }

        public bool UpdateState(NodeModel node)
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
            return Dirty;
        }

        public void UpdateGeometry()
        {
            GeometryUpdated = true;
            Dirty = true;
        }
    }

}
