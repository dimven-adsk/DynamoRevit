using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Autodesk.DesignScript.Interfaces;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.DirectContext3D;
using Autodesk.Revit.DB.ExternalService;
using Dynamo.Visualization;
using RevitServices.Persistence;

namespace Dynamo.Applications.Preview
{
    public sealed class PreviewServer : IDirectContext3DServer, IDisposable
    {
        private static Dictionary<Guid, PreviewServer> activeServers = new Dictionary<Guid, PreviewServer>();

        private bool disposed = false;
        private Guid _serverId;
        private Dictionary<Guid, NodePreviewObject> _previewObjects = new Dictionary<Guid, NodePreviewObject>();
        private Outline _outline;
        //private BufferPools _pools;
        bool _outlineDirty = true;
        private RenderEffects _selectedEffect;
        private RenderEffects _basicEffect;
        private DisplayStyle _effectStyle;

        private object _renderMutex;

        private PreviewServer(BufferPools pools)
        {
            _effectStyle = DisplayStyle.Undefined;
            _serverId = Guid.NewGuid();
            _renderMutex = new object();
            //_pools = pools;
        }

        public bool CanExecute(View dBView)
        {
            return dBView.ViewType == ViewType.FloorPlan ||
                dBView.ViewType == ViewType.AreaPlan ||
                dBView.ViewType == ViewType.Detail ||
                dBView.ViewType == ViewType.DraftingView ||
                dBView.ViewType == ViewType.Elevation ||
                dBView.ViewType == ViewType.FloorPlan ||
                dBView.ViewType == ViewType.Section ||
                dBView.ViewType == ViewType.ThreeD ||
                dBView.ViewType == ViewType.Walkthrough;
        }

        public string GetApplicationId() => "";

        public Outline GetBoundingBox(View dBView) 
        {
            if (!_outlineDirty)
                return _outline;

            _outline = null;
            lock(_renderMutex)
            {
                foreach(var item in _previewObjects.Values)
                    OutlineWrapper.UpdateOrCreateNew(ref _outline, item?.Outline);
            }

            _outlineDirty = _outline == null;
            return _outline;
        }

        public string GetDescription() => "Dynamo preview server";

        public string GetName() => "Dynamo preview server new";

        public Guid GetServerId() => _serverId;

        public ExternalServiceId GetServiceId() => ExternalServices.BuiltInExternalServices.DirectContext3DService;

        public string GetSourceId() => "";

        public string GetVendorId() => "Dynamo team";

        public void InitEffects(DisplayStyle displayStyle)
        {
            if (displayStyle == _effectStyle && _selectedEffect.IsValid && _basicEffect.IsValid)
                return;
            _effectStyle = displayStyle;
            _selectedEffect?.Dispose();
            _basicEffect?.Dispose();

            var format = displayStyle switch {
                DisplayStyle.Shading => VertexFormatBits.PositionNormal,
                DisplayStyle.ShadingWithEdges => VertexFormatBits.PositionNormal,
                _ => VertexFormatBits.Position,
            };

            _selectedEffect = new RenderEffects { EdgeEffect = new EffectInstance(VertexFormatBits.Position), MeshEffect = new EffectInstance(format) };
            _selectedEffect.EdgeEffect.SetColor(new Color(255, 255, 0));
            _selectedEffect.EdgeEffect.SetTransparency(0.3);
            _selectedEffect.MeshEffect.SetColor(new Color(255, 255, 0));
            _selectedEffect.MeshEffect.SetDiffuseColor(new Color(255, 255, 0));
            _selectedEffect.MeshEffect.SetAmbientColor(new Color(255, 255, 0));
            _selectedEffect.MeshEffect.SetTransparency(0.3);

            _basicEffect = new RenderEffects { EdgeEffect = new EffectInstance(VertexFormatBits.Position), MeshEffect = new EffectInstance(format) };
            _basicEffect.EdgeEffect.SetColor(new Color(150, 150, 150));
            _basicEffect.EdgeEffect.SetTransparency(0.4);
            _basicEffect.MeshEffect.SetColor(new Color(150, 150, 150));
            _basicEffect.MeshEffect.SetTransparency(0.4);
        }

        public void RenderScene(View dBView, DisplayStyle displayStyle)
        {
            if (!DrawContext.IsTransparentPass())
                return;
            Debug.WriteLine($"Rendering for server {GetServerId()}");

            InitEffects(displayStyle);
            var xform = Transform.Identity; 
            lock(_renderMutex)
            {
                foreach(var key in _previewObjects.Keys)
                {
                    var item = _previewObjects[key];
                    if (!item.Visible)
                        continue;
                    if (item.Selected)
                    {
                        item.Render(xform, _selectedEffect);
                    }
                    else
                    {
                        item.Render(xform, _basicEffect);
                    }
                }
            }
        }

        public bool UseInTransparentPass(View dBView) => true; // TODO check for necessity?

        public bool UsesHandles() => false;

        internal static PreviewServer StartNewServer(BufferPools pools)
        {
            ExternalServiceId serviceId = ExternalServices.BuiltInExternalServices.DirectContext3DService;
            var directContext3dService = ExternalServiceRegistry.GetService(serviceId) as MultiServerService;

            var doc = DocumentManager.Instance.CurrentDBDocument;
            Debug.WriteLine($"doc null? {doc == null} {doc?.Title} ");
            //IList<Guid> activeList = directContext3dService.GetActiveServerIds(doc);
            IList<Guid> activeList = directContext3dService.GetActiveServerIds();

            var previewServer = new PreviewServer(pools);
            Debug.WriteLine($"Adding server {previewServer.GetServerId()} ...");
            activeServers.Add(previewServer.GetServerId(), previewServer);
            activeList.Add(previewServer.GetServerId());
            directContext3dService.AddServer(previewServer);
            directContext3dService.SetActiveServers(activeList);
            //directContext3dService.SetActiveServers(activeList, doc);
            Debug.WriteLine($"Server was added");

            return previewServer;
        }

        public void StopServer()
        {
            Dispose();
        }

        //internal void AddPreviewObject(IPreviewObject previewObject)
        //{
        //    //var outline = previewObject.Outline; 
        //    //if (_outline == null)
        //    //{
        //    //    _outline = new Outline(outline);
        //    //}
        //    //else
        //    //{
        //    //    _outline.AddPoint(outline.MinimumPoint);
        //    //    _outline.AddPoint(outline.MaximumPoint);
        //    //}
        //    _previewObjects.Add(previewObject);
        //    _outlineDirty = true;
        //}

        internal void WithNodeCache(Guid nodeGuid, Action<NodePreviewObject> action)
        {
            _outlineDirty = true;
            lock(_renderMutex)
            {
                NodePreviewObject cached;
                if (_previewObjects.TryGetValue(nodeGuid, out var cache))
                {
                    cached = cache as NodePreviewObject;
                }
                else
                {
                    cached = new NodePreviewObject(nodeGuid);
                    _previewObjects[nodeGuid] = cached;
                }
                action(cached);
            }
            _outlineDirty = true;
            //AddPreviewObject()
            //var cache = _previewObjects.OfType<BufferCache>().FirstOrDefault();
            //if (cache == null)
            //{
            //    cache = new BufferCache(_pools);
            //    AddPreviewObject(cache);
            //}
            //return cache;
        }

        internal void DeletePreview(Guid nodeGuid)
        {
            lock(_renderMutex)
            {
                if (_previewObjects.TryGetValue(nodeGuid, out var node))
                {
                    _previewObjects.Remove(nodeGuid);
                    node.Dispose();
                }
            }
        }

        public void Dispose()
        {
            if (disposed) 
                return;

            ExternalServiceId serviceId = ExternalServices.BuiltInExternalServices.DirectContext3DService;
            var directContext3dService = ExternalServiceRegistry.GetService(serviceId) as MultiServerService;

            var id = GetServerId();
            Debug.WriteLine($"Removing preview server {id} ...");
            if (!activeServers.Remove(id))
                return; // this server was already removed

            Debug.WriteLine($"Disposing {_previewObjects.Count} preview object(s)");
            lock(_renderMutex)
            {
                foreach (var item in _previewObjects.Values)
                    item.Dispose();
            }
            _outline?.Dispose();

            directContext3dService.RemoveServer(id);
            Debug.WriteLine($"Server was removed");
            disposed = true;
        }
    }

    internal interface IPreviewObject : IDisposable
    {
        void Render(Transform transform);
        Outline Outline { get; }
    }

    internal interface IPreviewObject2 : IDisposable
    {
        void Render(Transform transform, RenderEffects effects);
        Outline Outline { get; }
    }

    internal class RenderEffects : IDisposable
    {
        public required EffectInstance EdgeEffect { get; init; }
        public required EffectInstance MeshEffect { get; init; }
        public bool IsValid => EdgeEffect == null && EdgeEffect.IsValid() && MeshEffect == null && MeshEffect.IsValid();

        public void Dispose()
        {
            EdgeEffect.Dispose();
            MeshEffect.Dispose();
        }
    }

    internal class BufferPools
    {
        public BufferPool<VertexBuffer> VertexPool { get; }
        public BufferPool<IndexBuffer> IndexPool { get; }
        public BufferPool<byte[]> RawIndexPool { get; }
        public BufferPool<float[]> RawVertexPool { get; }

        public BufferPools()
        {
            VertexPool = new BufferPool<VertexBuffer>(16, NewVertexBuffer);
            IndexPool = new BufferPool<IndexBuffer>(16, NewIndexBuffer);
            RawIndexPool = new BufferPool<byte[]>(16, NewRawIndexBuffer);
            RawVertexPool = new BufferPool<float[]>(16, NewRawVertexBuffer);
        }

        private SizedBuffer<VertexBuffer> NewVertexBuffer(int capacity)
        {
            return new SizedBuffer<VertexBuffer>(capacity, new VertexBuffer(capacity));
        }

        private SizedBuffer<IndexBuffer> NewIndexBuffer(int capacity)
        {
            return new SizedBuffer<IndexBuffer>(capacity, new IndexBuffer(capacity));
        }

        private SizedBuffer<byte[]> NewRawIndexBuffer(int capacity)
        {
            return new SizedBuffer<byte[]>(capacity, new byte[capacity]);
        }

        private SizedBuffer<float[]> NewRawVertexBuffer(int capacity)
        {
            return new SizedBuffer<float[]>(capacity, new float[capacity]);
        }
    }

    internal class NodePreviewObject : IPreviewObject2
    {
        public Guid NodeGuid { get; set; }
        public bool Selected { get; set; } = false;
        public bool Visible { get; set; } = false;

        private Outline _outline;

        public Outline Outline => _outline;

        private BufferCache _edgeCache;
        private BufferCache _meshCache;
        private BufferCache _pointCache;

        public NodePreviewObject(Guid nodeGuid)
        {
            NodeGuid = nodeGuid;
        }

        public void Render(Transform transform, RenderEffects effects)
        {
            _edgeCache?.Render(transform, effects.EdgeEffect);
            _meshCache?.Render(transform, effects.MeshEffect);
            _pointCache?.Render(transform, effects.MeshEffect);
        }

        public void Clear()
        {
            // Using this instance after clear is fine, but wrap it for clarity
            Dispose();
        }

        public void Dispose()
        {
            _edgeCache?.Dispose();
            _meshCache?.Dispose();
            _pointCache?.Dispose();
            _outline?.Dispose();
            _edgeCache = null;
            _meshCache = null;
            _pointCache = null;
            _outline = null;
        }

        public void AddMesh(IRenderPackage meshRenderPackage)
        {
            if (_meshCache == null)
                _meshCache = new BufferCache(PrimitiveType.TriangleList);
            _meshCache.FromMeshRenderPackage(meshRenderPackage);
            OutlineWrapper.UpdateOrCreateNew(ref _outline, _meshCache.Outline);
        }

        public void AddEdge(IRenderPackage edgeRenderPackage)
        {
            if (_edgeCache == null)
                _edgeCache = new BufferCache(PrimitiveType.LineList);
            _edgeCache.FromLineRenderPackage(edgeRenderPackage);
            OutlineWrapper.UpdateOrCreateNew(ref _outline, _edgeCache.Outline);
        }
    }

    internal class LazyProtoPreview : IPreviewObject
    {
        protected IPreviewObject _inner = null;
        protected Func<IPreviewObject> _instantiate;

        public Outline Outline => _inner?.Outline;

        public LazyProtoPreview(Func<IPreviewObject> instantiate)
        {
            _instantiate = instantiate;
        }

        public void Dispose()
        {
            _inner?.Dispose();
            _inner = null;
        }

        public virtual void Render(Transform transform)
        {
            if (_inner == null)
                _inner = _instantiate();
            if (_inner == null)
                return;
            _inner.Render(transform);
        }
    }

    internal class LazyInstanceProtoPreview : LazyProtoPreview
    {
        private List<Transform> _transforms;
        public LazyInstanceProtoPreview(Func<InstanceProtoPreview> instantiateObject)
            : base(instantiateObject)
        {
            _transforms = new List<Transform>();
        }

        public void AddTransform(Transform transform)
        {
            _transforms.Add(transform);
        }

        public override void Render(Transform transform)
        {
            if (_inner == null)
            {
                _inner = _instantiate();
                if (_inner == null)
                    return;
                var instance = (InstanceProtoPreview)_inner;
                foreach (var xform in _transforms)
                    instance.AddTransform(xform);
            }
            base.Render(transform);
        }
    }

    internal class ProtoPreview : IDisposable, IPreviewObject
    {
        class DoublesToXYZ
        {
            List<double> _vertices;
            private double _scale = 1;
            public int Count => _vertices.Count / 3;
            public Outline Outline { get; }
            public DoublesToXYZ(IEnumerable<double> vertices, bool withOutline)
            {
                _vertices = vertices.ToList();
                if (withOutline)
                    Outline = new Outline(GetXYZ(0), GetXYZ(1));
            }

            public DoublesToXYZ(IEnumerable<double> vertices, bool withOutline, double scale)
            {
                _vertices = vertices.ToList();
                _scale = scale;
                if (withOutline)
                    Outline = new Outline(GetXYZ(0), GetXYZ(1));
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public XYZ GetXYZ(int index)
            {
                int idx = index * 3;
                return new XYZ(_vertices[idx], _vertices[idx + 1], _vertices[idx + 2]);
            }

            public XYZ GetVertex(int index)
            {
                int idx = index * 3;
                var xyz = new XYZ(_vertices[idx] * _scale, _vertices[idx + 1] * _scale, _vertices[idx + 2] * _scale);
                Outline.AddPoint(xyz);
                return xyz;
            }
        }

        class BytesToColor
        {
            List<byte> _colors;
            public int Count => _colors.Count / 4;
            public BytesToColor(IEnumerable<byte> colors)
            {
                _colors = colors.ToList();
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ColorWithTransparency GetColor(int index)
            {
                var idx = index * 4;
                return new ColorWithTransparency(_colors[idx], _colors[idx + 1], _colors[idx + 2], _colors[idx + 3]);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ColorWithTransparency GetAColor(int index)
            {
                var v = (byte)Random.Shared.Next(255);
                return new ColorWithTransparency(v, v, v, 200);
            }
        }

        private VertexFormatBits _format;
        private PrimitiveType _type;
        private int _vertsPerPrimitive;
        private int _vertexCount;
        private int _indexCount;
        private int _primitiveCount;
        private VertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;
        private VertexFormat _vertexFormat;
        private EffectInstance _effectInstance;
        private Outline _outline;

        private bool disposedValue = false;

        public Outline Outline { get => _outline; private set => _outline = value; }
        public EffectInstance EffectInstance {  get => _effectInstance; private set => _effectInstance = value; }
        public VertexFormatBits FormatBits => _format;

        public ProtoPreview(PrimitiveType type, int vertexCount, bool hasNormals, bool hasColors)
        {
            var (bits, vertSize) = (hasNormals, hasColors) switch
            {
                (true, true) => (VertexFormatBits.PositionNormalColored, VertexPositionNormalColored.GetSizeInFloats()),
                (true, false) => (VertexFormatBits.PositionNormal, VertexPositionNormal.GetSizeInFloats()),
                (false, true) => (VertexFormatBits.PositionColored, VertexPositionColored.GetSizeInFloats()),
                (false, false) => (VertexFormatBits.Position, VertexPosition.GetSizeInFloats()),
            };
            var (vertsPerPrimitive, indexSize) = type switch
            {
                PrimitiveType.TriangleList => (3, IndexTriangle.GetSizeInShortInts()),
                PrimitiveType.LineList => (2, IndexLine.GetSizeInShortInts()),
                PrimitiveType.PointList => (1, IndexPoint.GetSizeInShortInts()),
                _ => throw new NotImplementedException(),
            };

            _format = bits;
            _type = type;

            //_vertexFormat = new VertexFormat(bits);
            //_effectInstance = new EffectInstance(bits);
            
            _vertsPerPrimitive = vertsPerPrimitive;
            _vertexCount = vertexCount;
            _primitiveCount = _vertexCount / _vertsPerPrimitive;
            _indexCount = _primitiveCount;

            _vertexBuffer = new VertexBuffer(_vertexCount * vertSize);
            _vertexBuffer.Map(_vertexCount * vertSize);

            _indexBuffer = new IndexBuffer(_vertexCount * indexSize);
            _indexBuffer.Map(_vertexCount * indexSize);
        }

        public void Render(Transform transform)
        {
            var effectInstance = new EffectInstance(_format);
            effectInstance.SetTransparency(0.5);
            //effectInstance.SetColor(new Color(100, 100, 150));
            //effectInstance.SetDiffuseColor(new Color(100, 100, 150));
            //effectInstance.SetAmbientColor(new Color(100, 100, 150));
            //effectInstance.SetSpecularColor(new Color(255, 255, 255));
            //effectInstance.SetGlossiness(0.1);
            DrawContext.SetWorldTransform(transform);
            DrawContext.FlushBuffer(
                _vertexBuffer,
                _vertexCount,
                _indexBuffer,
                _indexCount,
                new VertexFormat(_format),
                effectInstance,
                _type,
                0,
                _primitiveCount
                );
        }

        public static ProtoPreview FromSolid(Autodesk.DesignScript.Geometry.Solid solid, IRenderPackageFactory renderPackageFactory)
        {
            var pkg = renderPackageFactory.CreateRenderPackage();
            solid.Tessellate(pkg, renderPackageFactory.TessellationParameters);

            return FromMeshRenderPackage(pkg);
        }

        public static ProtoPreview FromMeshRenderPackage(IRenderPackage pkg)
        {
            var scale = Revit.GeometryConversion.UnitConverter.DynamoToHostFactor(SpecTypeId.Length);

            var verts = new DoublesToXYZ(pkg.MeshVertices, true, scale);
            var numVerts = verts.Count;
            var numTris = numVerts / 3;
            var hasVerts = numVerts > 0;
            var colors = new BytesToColor(pkg.MeshVertexColors);
            var hasColors = colors.Count == numVerts;
            hasColors = false;
            var normals = new DoublesToXYZ(pkg.MeshNormals, false);
            var hasNormals = normals.Count == numVerts;
            //hasNormals = false;


            var preview = new ProtoPreview(PrimitiveType.TriangleList, numVerts, hasNormals, hasColors);
            var indexBuffer = preview._indexBuffer;
            var vertexBuffer = preview._vertexBuffer;
            var bits = preview.FormatBits;

            using (var idxStream = indexBuffer.GetIndexStreamTriangle())
            using (var tri = new IndexTriangle(0, 0, 0))
            {
                if (bits == VertexFormatBits.Position)
                {
                    using (var stream = vertexBuffer.GetVertexStreamPosition())
                    using (var vert = new VertexPosition(XYZ.Zero))
                    {
                        for (int j = 0; j < numTris; j++)
                        {
                            var v = j * 3;
                            var (j0, j1, j2) = (v, v + 1, v + 2); // => (0, 1, 2), (3, 4, 5) ...
                            vert.Position = verts.GetVertex(j0); stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j1); stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j2); stream.AddVertex(vert);
                            tri.Index0 = j0; tri.Index1 = j1; tri.Index2 = j2;
                            idxStream.AddTriangle(tri);
                        }
                    }
                }
                else if (bits == VertexFormatBits.PositionNormal)
                {
                    using (var stream = vertexBuffer.GetVertexStreamPositionNormal())
                    using (var vert = new VertexPositionNormal(XYZ.Zero, XYZ.Zero))
                    {
                        for (int j = 0; j < numTris; j++)
                        {
                            var v = j * 3;
                            var (j0, j1, j2) = (v, v + 1, v + 2); // => (0, 1, 2), (3, 4, 5) ...
                            vert.Position = verts.GetVertex(j0); vert.Normal = normals.GetXYZ(j0); stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j1); vert.Normal = normals.GetXYZ(j1); stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j2); vert.Normal = normals.GetXYZ(j2); stream.AddVertex(vert);
                            tri.Index0 = j0; tri.Index1 = j1; tri.Index2 = j2;
                            idxStream.AddTriangle(tri);
                        }
                    }
                }
                else if (bits == VertexFormatBits.PositionColored)
                {
                    using (var stream = vertexBuffer.GetVertexStreamPositionColored())
                    using (var vert = new VertexPositionColored(XYZ.Zero, new ColorWithTransparency()))
                    {
                        for (int j = 0; j < numTris; j++)
                        {
                            var v = j * 3;
                            var (j0, j1, j2) = (v, v + 1, v + 2); // => (0, 1, 2), (3, 4, 5) ...
                            vert.Position = verts.GetVertex(j0); vert.SetColor(colors.GetAColor(j0)); stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j1); vert.SetColor(colors.GetAColor(j1)); stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j2); vert.SetColor(colors.GetAColor(j2)); stream.AddVertex(vert);
                            tri.Index0 = j0; tri.Index1 = j1; tri.Index2 = j2;
                            idxStream.AddTriangle(tri);
                        }
                    }
                }
                else if (bits == VertexFormatBits.PositionNormalColored)
                {
                    using (var stream = vertexBuffer.GetVertexStreamPositionNormalColored())
                    using (var vert = new VertexPositionNormalColored(XYZ.Zero, XYZ.Zero, new ColorWithTransparency()))
                    {
                        for (int j = 0; j < numTris; j++)
                        {
                            var v = j * 3;
                            var (j0, j1, j2) = (v, v + 1, v + 2); // => (0, 1, 2), (3, 4, 5) ...
                            vert.Position = verts.GetVertex(j0); vert.Normal = normals.GetXYZ(j0); vert.SetColor(colors.GetAColor(j0)); stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j1); vert.Normal = normals.GetXYZ(j1); vert.SetColor(colors.GetAColor(j1)); stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j2); vert.Normal = normals.GetXYZ(j2); vert.SetColor(colors.GetAColor(j2)); stream.AddVertex(vert);
                            tri.Index0 = j0; tri.Index1 = j1; tri.Index2 = j2;
                            idxStream.AddTriangle(tri);
                        }
                    }
                }
            }
            indexBuffer.Unmap();
            vertexBuffer.Unmap();

            preview.Outline = verts.Outline;
            //Debug.WriteLine($"Created preview item");
            return preview;
        }


        public static ProtoPreview FromLineRenderPackage(IRenderPackage pkg)
        {
            var scale = Revit.GeometryConversion.UnitConverter.DynamoToHostFactor(SpecTypeId.Length);

            var verts = new DoublesToXYZ(pkg.LineStripVertices, true, scale);
            var numVerts = verts.Count;
            var hasVerts = numVerts > 0;
            var colors = new BytesToColor(pkg.LineStripVertexColors);
            var hasColors = colors.Count == numVerts;
            var lineIndices = pkg.LineStripIndices.ToList();
            var numLines = lineIndices.Count;

            var preview = new ProtoPreview(PrimitiveType.LineList, numVerts, false, hasColors);
            var indexBuffer = preview._indexBuffer;
            var vertexBuffer = preview._vertexBuffer;
            var bits = preview.FormatBits;

            using (var idxStream = indexBuffer.GetIndexStreamLine())
            using (var line = new IndexLine(0, 0))
            {
                if (bits == VertexFormatBits.Position)
                {
                    using (var stream = vertexBuffer.GetVertexStreamPosition())
                    using (var vert = new VertexPosition(XYZ.Zero))
                    {
                        for (int j = 0; j < numVerts; j += 2)
                        {
                            var j1 = j + 1;
                            vert.Position = verts.GetVertex(j);  stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j1);  stream.AddVertex(vert);
                            line.Index0 = j; line.Index1 = j1;
                            idxStream.AddLine(line);
                        }
                    }
                }
                else if (bits == VertexFormatBits.PositionColored)
                {
                    using (var stream = vertexBuffer.GetVertexStreamPositionColored())
                    using (var vert = new VertexPositionColored(XYZ.Zero, new ColorWithTransparency()))
                    {
                        for (int j = 0; j < numVerts; j++)
                        {
                            var j1 = j + 1;
                            vert.Position = verts.GetVertex(j);  vert.SetColor(colors.GetColor(j)); stream.AddVertex(vert);
                            vert.Position = verts.GetVertex(j1);  vert.SetColor(colors.GetColor(j1)); stream.AddVertex(vert);
                            line.Index0 = j; line.Index1 = j1;
                            idxStream.AddLine(line);
                        }
                    }
                }
            }
            indexBuffer.Unmap();
            vertexBuffer.Unmap();

            preview.Outline = verts.Outline;
            return preview;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _vertexBuffer?.Dispose();
                    _indexBuffer?.Dispose();
                    _vertexFormat?.Dispose();
                    _effectInstance?.Dispose();
                    _outline?.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    internal class InstanceProtoPreview : IPreviewObject
    {
        private ProtoPreview _instanceGeometry;
        private XYZ[] _outlinePoints;
        private List<Transform> _transforms;

        public Outline Outline { get; private set; }

        public InstanceProtoPreview(ProtoPreview instancePreviewGeometry)
        {
            _instanceGeometry = instancePreviewGeometry;
            var min = _instanceGeometry.Outline.MinimumPoint;
            var max = _instanceGeometry.Outline.MaximumPoint;
            var (ax, ay, az) = (min.X, min.Y, min.Z);
            var (bx, by, bz) = (max.X, max.Y, max.Z);
            _outlinePoints =
            [
                new XYZ(ax, ay, az), // 0 0 0
                new XYZ(ax, ay, bz), // 0 0 1
                new XYZ(ax, by, bz), // 0 1 1
                new XYZ(ax, by, az), // 0 1 0
                new XYZ(bx, ay, az), // 1 0 0
                new XYZ(bx, by, az), // 1 1 0
                new XYZ(bx, ay, bz), // 1 0 1
                new XYZ(bx, by, bz), // 1 1 1
            ];
        }

        public void AddTransform(Transform transform)
        {
            if (_transforms == null)
            {
                _transforms = new List<Transform>();
                Outline = new Outline(transform.OfPoint(_outlinePoints[0]), transform.OfPoint(_outlinePoints[1]));
            }
            for (int i = 0; i < 8; i++)
                Outline.AddPoint(transform.OfPoint(_outlinePoints[i]));

            _transforms.Add(transform);
        }

        public void Render(Transform transform)
        {
            if (_transforms == null)
                return;
            foreach (var xform in _transforms)
                _instanceGeometry.Render(xform);
        }

        public void Dispose()
        {
            _instanceGeometry?.Dispose();
            _instanceGeometry = null;
        }
    }
    
    internal class BufferCache : IPreviewObject
    {
        private Outline _outline;
        private List<BufferedProtoPreview> _buffers;
        //private BufferPools _pools;
        private PrimitiveType _type;

        double _modelScale = double.NaN;

        public BufferCache(PrimitiveType type)
        {
            //_pools = pools;
            _type = type;
            _buffers = new List<BufferedProtoPreview>();
        }

        public Outline Outline => _outline;

        public void Dispose()
        {
            _outline?.Dispose();
            foreach(var buffer in _buffers)
                buffer.Dispose();
        }
        
        public BufferedProtoPreview GetBufferWithCapacity(int capacity)
        {
            // get the last buffer in the list of buffers, as it should always
            // be the one with the highest capacity
            var bufCount = _buffers.Count;
            if (bufCount > 0 && _buffers[bufCount - 1].Capacity > capacity)
                return _buffers[bufCount - 1];

            Debug.WriteLine($"Creating buffer {bufCount + 1}");
            var newBuffer = new BufferedProtoPreview(_type);
            _buffers.Add(newBuffer);
            return newBuffer;
        }

        public void UpdateCache(BufferedProtoPreview buffer)
        {
            OutlineWrapper.UpdateOrCreateNew(ref _outline, buffer.Outline);

            // the returned buffer must always be the last buffer in the list
            var bufCount = _buffers.Count;
            Debug.Assert(ReferenceEquals(buffer, _buffers[bufCount - 1]));
            if (bufCount > 1)
            {
                var capacity = buffer.Capacity;
                var idx = bufCount - 2;
                while (idx >= 0)
                {
                    var other = _buffers[idx];
                    if (other.Capacity > capacity)
                    {
                        _buffers[idx + 1] = other;
                        _buffers[idx] = buffer;
                        idx--;
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        public void Render(Transform transform)
        {
            var interrupted = DrawContext.IsInterrupted();
            var numPrimitives = 0;
            foreach(var item in _buffers)
            {
                if (interrupted) 
                    return;

                numPrimitives += item.PrimitiveCount;
                item.Render(transform);
            }
            //Debug.WriteLine($"Cache has {numPrimitives} triangle(s)");
        }

        public void Render(Transform transform, EffectInstance effectInstance)
        {
            var interrupted = DrawContext.IsInterrupted();
            var numPrimitives = 0;
            foreach(var item in _buffers)
            {
                if (interrupted) 
                    return;

                numPrimitives += item.PrimitiveCount;
                item.Render(transform, effectInstance);
            }
            //Debug.WriteLine($"Cache has {numPrimitives} triangle(s)");
        }


        public BufferedProtoPreview FromMeshRenderPackage(IRenderPackage pkg)
        {
            if (double.IsNaN(_modelScale))
                _modelScale = Revit.GeometryConversion.UnitConverter.DynamoToHostFactor(SpecTypeId.Length);
            //_modelScale = 1;

            var max = ushort.MaxValue + 1;
            var vertexComponents = pkg.MeshVertices.ToArray();
            var normalComponents = pkg.MeshNormals.ToArray();
            var numVertices = vertexComponents.Length / 3;
            var numTris = numVertices / 3;
            //var remainingTris = numTris;
            var remainingVerts = numVertices;
            var end = 0;
            BufferedProtoPreview buffer = null;
            while (remainingVerts > 0)
            {
                try
                {
                    var start = end;
                    var bufCapacity = (Math.Min(remainingVerts, max) / 3) * 3;
                    remainingVerts -= bufCapacity;
                    end = start + bufCapacity;
                    buffer = GetBufferWithCapacity(bufCapacity * 6); // buffer for 3 vertices per tri, and 6 components per vertex
                    buffer.AppendMeshParts(
                        new ReadOnlySpan<double>(vertexComponents, start * 3, bufCapacity * 3), 
                        new ReadOnlySpan<double>(normalComponents, start * 3, bufCapacity * 3),
                        _modelScale);
                    UpdateCache(buffer);

                } catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                    Debug.WriteLine(ex.StackTrace);
                    throw;
                }
            }
            return buffer;
        }

        internal void FromLineRenderPackage(IRenderPackage edgeRenderPackage)
        {
            if (double.IsNaN(_modelScale))
                _modelScale = Revit.GeometryConversion.UnitConverter.DynamoToHostFactor(SpecTypeId.Length);

            var max = ushort.MaxValue + 1;
            var vertexComponents = edgeRenderPackage.LineStripVertices.ToArray();
            var indices = edgeRenderPackage.LineStripIndices.ToArray();
            var clampedIndexLength = indices.Length & ~1; // unset the 1-bit to make divisible by 2

            var numVertices = vertexComponents.Length / 3;
            BufferedProtoPreview buffer = null;

            var indexBuffer = new int[max - 2]; // can't use the 65k buffer for some reason
            var currentIndex = 0;
            var bufferIndex = 0;
            var vertexStart = indices[0];
            while (currentIndex < clampedIndexLength)
            {
                var a = indices[currentIndex++];
                var b = indices[currentIndex++];

                indexBuffer[bufferIndex++] = a - vertexStart;
                indexBuffer[bufferIndex++] = b - vertexStart;

                // either at buffer capacity, or at end of index array
                if (bufferIndex >= indexBuffer.Length || currentIndex >= clampedIndexLength)
                {
                    var vertexEnd = b + 1;
                    var numVerts = vertexEnd - vertexStart;
                    var vertexSpan = new ReadOnlySpan<double>(vertexComponents, vertexStart * 3, numVerts * 3);
                    var indexSpan = new ReadOnlySpan<int>(indexBuffer, 0, bufferIndex);

                    try
                    {
                        buffer = GetBufferWithCapacity(vertexSpan.Length); // buffer for 2 vertices per line, and 3 components per vertex
                        buffer.AppendLineSegments(vertexSpan, indexSpan, _modelScale);
                        Debug.WriteLine($"Linecount {buffer.PrimitiveCount}");
                        UpdateCache(buffer);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex.ToString());
                        Debug.WriteLine(ex.StackTrace);
                        throw;
                    }
                    bufferIndex = 0;
                    if (currentIndex < clampedIndexLength)
                        vertexStart = indices[currentIndex];
                }
            }
        }
    }

    internal struct SizedBuffer<T>
    {
        public int Size { get; }
        public T Buffer { get; }
        public SizedBuffer(int size, T buffer)
        {
            Size = size;
            Buffer = buffer;
        }
    }

    internal class BufferPool<T>
    {
        private object _bufferMutex;
        private int _poolSize;
        private Dictionary<int, List<SizedBuffer<T>>> _buffers;
        private Func<int, SizedBuffer<T>> _factory;

        public BufferPool(int poolSize, Func<int, SizedBuffer<T>> bufferFactory)
        {
            _factory = bufferFactory;
            _buffers = new Dictionary<int, List<SizedBuffer<T>>>();
            _poolSize = poolSize;
        }

        public int GetBufferPoolIdx(int targetCapacity, int mult)
        {
            var bufferLogSize = Math.Log2(targetCapacity / mult);
            var bufferTargetSize = (int)bufferLogSize;
            if (bufferLogSize % 1 > 0)
                bufferTargetSize++;
            return bufferTargetSize;
        }

        public SizedBuffer<T> RentBuffer(int capacity, int mult)
        {
            var bufferTargetPool = GetBufferPoolIdx(capacity, mult);
            lock (_bufferMutex)
            {
                if (_buffers.TryGetValue(bufferTargetPool, out var rentableBuffers) && rentableBuffers.Count > 0)
                {
                    var idx = rentableBuffers.Count - 1;
                    var buffer = rentableBuffers[idx];
                    rentableBuffers.RemoveAt(idx);
                    return buffer;
                }
                else
                {
                    var bufferSize = (int)Math.Pow(2, bufferTargetPool);
                    var buffer = _factory(bufferSize * mult);
                    return buffer;
                }
            }
        }

        public bool ReturnBuffer(SizedBuffer<T> buffer, int mult)
        {
            var bufferTargetPool = GetBufferPoolIdx(buffer.Size, mult);

            lock (_bufferMutex)
            {
                if (_buffers.TryGetValue(bufferTargetPool, out var buffers) && buffers.Count > 0)
                {
                    if (buffers.Count < _poolSize)
                    {
                        buffers.Add(buffer);
                        return true;
                    }
                    return false; // no space to store the buffer
                }
                else
                {
                    _buffers[bufferTargetPool] = new List<SizedBuffer<T>> { buffer };
                    return true;
                }
            }
        }

        public bool ReturnBufferOrDispose(SizedBuffer<T> buffer, int mult)
        {
            var bufferTargetPool = GetBufferPoolIdx(buffer.Size, mult);

            lock (_bufferMutex)
            {
                if (_buffers.TryGetValue(bufferTargetPool, out var buffers) && buffers.Count > 0)
                {
                    if (buffers.Count < _poolSize)
                    {
                        buffers.Add(buffer);
                        return true;
                    }
                    if (buffer.Buffer is IDisposable disposable)
                        disposable.Dispose();

                    return false; // no space to store the buffer
                }
                else
                {
                    _buffers[bufferTargetPool] = new List<SizedBuffer<T>> { buffer };
                    return true;
                }
            }
        }
    }

    internal class BufferedProtoPreview : IPreviewObject
    {
        const int MAX_SIZE = ushort.MaxValue + 1;

        private float[] _vertexComponents;
        private int _vertexComponentCount;

        private byte[] _indices; // this should be a ushort[], but C# Marshal.Copy does not support ushort[] => IntPtr
        private int _indexComponentCount; // this offset is in ushorts, so the buffer fill length will be _indexOffset * 2

        private bool _initialized = false;
        private bool _synced = false;

        private (double, double, double) _min = (double.MaxValue, double.MaxValue, double.MaxValue);
        private (double, double, double) _max = (double.MinValue, double.MinValue, double.MinValue);

        private SizedBuffer<VertexBuffer> _sizedVertexBuffer;
        private VertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;
        private VertexFormat _vertexFormat;
        private EffectInstance _effectInstance;

        //private BufferPools _pools;

        private Outline _outline;

        private PrimitiveType _type;
        private int _vertexSize;
        private int _indexSize;

        public int Capacity => (MAX_SIZE * _vertexSize) - _vertexComponentCount;
        public int PrimitiveCount => _indexComponentCount / _indexSize; // 3 for triangles

        public bool HasValidBuffers => _vertexBuffer != null && _vertexBuffer.IsValid()
            && _indexBuffer != null && _indexBuffer.IsValid()
            && _vertexFormat != null && _vertexFormat.IsValid();

        public Outline Outline
        {
            get
            {
                UpdateOutline();
                return _outline;
            }
        }

        internal BufferedProtoPreview(PrimitiveType primitiveType)
        {
            //Debug.WriteLine($"actual {VertexBufferSize} expected {MAX_SIZE * 6}");
            //_pools = pools;
            _type = primitiveType;
            (_vertexSize, _indexSize) = _type switch
            {
                PrimitiveType.TriangleList => (6, 3),
                PrimitiveType.LineList => (3, 2),
                _ => throw new NotImplementedException()
            };
            // unsure if the max size is the size of the backing byte buffer, or the amount of floats
            //var rawComponents = _pools.RawVertexPool.RentBuffer(MAX_SIZE * _vertexSize, _vertexSize); // new float[VertexBufferSize];
            _vertexComponents = new float[MAX_SIZE * _vertexSize];
            //var rawIndices = _pools.RawIndexPool.RentBuffer(MAX_SIZE * _indexSize * 2, _indexSize * 2); // new float[VertexBufferSize];
            _indices = new byte[MAX_SIZE * _indexSize * 2]; 
        }

        public void UpdateOutline()
        {
            if (_min.Item1 > _max.Item1)
                return;
            var pMin = new XYZ(_min.Item1, _min.Item2, _min.Item3);
            var pMax = new XYZ(_max.Item1, _max.Item2, _max.Item3);
            OutlineWrapper.UpdateOrCreateNew(ref _outline, pMin, pMax);
        }

        private void InitializeRenderBuffers()
        {
            //var rawVertexBuffer = _pools.VertexPool.RentBuffer(MAX_SIZE * _vertexSize, _vertexSize);
            _vertexBuffer = new VertexBuffer(MAX_SIZE * _vertexSize);
            //var rawIndexBuffer = _pools.IndexPool.RentBuffer(MAX_SIZE * _indexSize, _indexSize);
            _indexBuffer = new IndexBuffer(MAX_SIZE * _indexSize);

            _vertexFormat = _type switch
            {
                PrimitiveType.TriangleList => new VertexFormat(VertexFormatBits.PositionNormal),
                PrimitiveType.LineList => new VertexFormat(VertexFormatBits.Position),
                _ => throw new NotImplementedException()
            };
            //_vertexFormat = new VertexFormat(VertexFormatBits.PositionNormal);
            _initialized = true;
        }

        private void CopyBuffers()
        {
            _vertexBuffer.Map(_vertexComponentCount);
            Marshal.Copy(_vertexComponents, 0, _vertexBuffer.GetMappedHandle(), _vertexComponentCount);
            _vertexBuffer.Unmap();

            var byteOffset = _indexComponentCount * 2;
            _indexBuffer.Map(_indexComponentCount);
            //Debug.WriteLine($"index buf {_indexOffset}");
            Marshal.Copy(_indices, 0, _indexBuffer.GetMappedHandle(), byteOffset);
            _indexBuffer.Unmap();
            _synced = true;
        }

        internal void AppendMeshParts(ReadOnlySpan<double> vertexComponents, ReadOnlySpan<double> normalComponents, double scale)
        {
            _synced = false;
            // assume we have a normal for each vertex
            var numVertices = vertexComponents.Length / 3;
            var numTriangles = numVertices / _indexSize;
            var vertIndex = (ushort)(_vertexComponentCount / _vertexSize);
            var idxSpan = MemoryMarshal.Cast<byte, ushort>(_indices);
            var (xMin, yMin, zMin) = _min;
            var (xMax, yMax, zMax) = _max;
            for (int i = 0; i < numVertices; i++) 
            {
                var j = i * 3;
                var x = vertexComponents[j++] * scale;
                //var y = vertexComponents[j++] * scale;
                //var z = vertexComponents[j] * scale;
                var z = vertexComponents[j++] * scale;
                var y = -vertexComponents[j] * scale;
                xMin = Math.Min(xMin, x);
                yMin = Math.Min(yMin, y);
                zMin = Math.Min(zMin, z);
                xMax = Math.Max(xMax, x);
                yMax = Math.Max(yMax, y);
                zMax = Math.Max(zMax, z);
                _vertexComponents[_vertexComponentCount++] = (float)x;
                _vertexComponents[_vertexComponentCount++] = (float)y;
                _vertexComponents[_vertexComponentCount++] = (float)z;
                
                j = i * 3; // restart the component index for the normals
                var nx = (float)normalComponents[j++];
                //var ny = (float)normalComponents[j++];
                //var nz = (float)normalComponents[j];
                var nz = (float)normalComponents[j++];
                var ny = -(float)normalComponents[j];
                _vertexComponents[_vertexComponentCount++] = nx;
                _vertexComponents[_vertexComponentCount++] = ny;
                _vertexComponents[_vertexComponentCount++] = nz;
                //_componentOffset++; // add one for color
            }

            for (int i = 0; i < numTriangles; i++)
            { 
                // TODO: check if these need to use the pkg.MeshIndices variable?
                idxSpan[_indexComponentCount++] = vertIndex++;
                idxSpan[_indexComponentCount++] = vertIndex++;
                idxSpan[_indexComponentCount++] = vertIndex++;
                //var byteValue = BitConverter.ToUInt16(new ReadOnlySpan<byte>(_indices, (_indexOffset - 1) * 2, 2));
                //Debug.WriteLine($"Written value: {vertIndex}, stored value: {byteValue}");
            }

            _min = (xMin, yMin, zMin);
            _max = (xMax, yMax, zMax);
        }

        internal void AppendLineSegments(ReadOnlySpan<double> vertexComponents, ReadOnlySpan<int> segmentIndices, double scale)
        {
            _synced = false;
            // assume we have a normal for each vertex
            var numVertices = vertexComponents.Length / 3;
            var numLines = numVertices / _indexSize;
            var vertexIndex = (ushort)(_vertexComponentCount / _vertexSize);
            var idxSpan = MemoryMarshal.Cast<byte, ushort>(_indices);
            var (xMin, yMin, zMin) = _min;
            var (xMax, yMax, zMax) = _max;
            for (int i = 0; i < numVertices; i++) 
            {
                var j = i * 3;
                var x = vertexComponents[j++] * scale;
                //var y = vertexComponents[j++] * scale;
                //var z = vertexComponents[j] * scale;
                var z = vertexComponents[j++] * scale; 
                var y = -vertexComponents[j] * scale;
                xMin = Math.Min(xMin, x);
                yMin = Math.Min(yMin, y);
                zMin = Math.Min(zMin, z);
                xMax = Math.Max(xMax, x);
                yMax = Math.Max(yMax, y);
                zMax = Math.Max(zMax, z);
                _vertexComponents[_vertexComponentCount++] = (float)x;
                _vertexComponents[_vertexComponentCount++] = (float)y;
                _vertexComponents[_vertexComponentCount++] = (float)z;
            }

            var startOffset = segmentIndices[0]; // likely not necessary?
            for (int i = 0; i < segmentIndices.Length; i += 2)
            {
                var a = segmentIndices[i] - startOffset;
                var b = segmentIndices[i + 1] - startOffset;
                idxSpan[_indexComponentCount++] = (ushort)(a + vertexIndex);
                idxSpan[_indexComponentCount++] = (ushort)(b + vertexIndex);
            }

            _min = (xMin, yMin, zMin);
            _max = (xMax, yMax, zMax);
        }

        public void Dispose()
        {
            DestroyBuffers();
        }

        private void DestroyBuffers()
        {
            _vertexBuffer?.Dispose();
            _indexBuffer?.Dispose();
            //_vertexFormat?.Dispose();
            _outline?.Dispose();
            _vertexBuffer = null;
            _indexBuffer = null;
            //_vertexFormat = null;
            _outline = null;

            _initialized = false;
        }

        public void Render(Transform transform)
        {
            _effectInstance ??= new EffectInstance(VertexFormatBits.PositionNormal);
            Render(transform, _effectInstance);
        }

        public void Render(Transform transform, EffectInstance effectInstance)
        {
            if (!HasValidBuffers)
                DestroyBuffers(); // the old buffers have been invalidated, remove them

            if (!_initialized)
                InitializeRenderBuffers(); // ensure we have buffers to render

            if (!_synced)
                CopyBuffers(); // ensure the buffer content is up-to-date

            DrawContext.SetWorldTransform(transform);
            var primitiveCount = _indexComponentCount / _indexSize;
            DrawContext.FlushBuffer(
                _vertexBuffer,
                _vertexComponentCount / _vertexSize,
                _indexBuffer,
                _indexComponentCount,
                _vertexFormat,
                effectInstance,
                _type,
                0,
                primitiveCount
            );
        }
    }

    internal class OutlineWrapper
    {
        private Outline _outline = null;
        public Outline Outline => _outline;

        public void UpdateOutline(Outline other)
        {
            if (other == null || !other.IsValidObject)
                return;
            UpdateOrCreateNew(ref _outline, other.MinimumPoint, other.MaximumPoint);
        }

        public static void UpdateOrCreateNew(ref Outline current, XYZ minPoint, XYZ maxPoint)
        {
            if (current == null)
            {
                current = new Outline(minPoint, maxPoint);
            }
            else
            {
                current.AddPoint(minPoint);
                current.AddPoint(maxPoint);
            }
        }
        public static void UpdateOrCreateNew(ref Outline current, Outline other)
        {
            if (other == null || !other.IsValidObject)
                return;
            UpdateOrCreateNew(ref current, other.MinimumPoint, other.MaximumPoint);
        }
    }
}
