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
        private readonly static Dictionary<Guid, PreviewServer> activeServers = new Dictionary<Guid, PreviewServer>();

        private bool disposed = false;
        private readonly Guid _serverId;

        private readonly Dictionary<Guid, NodePreviewObject> _previewObjects = new Dictionary<Guid, NodePreviewObject>();
        private Outline _outline;
        //private BufferPools _pools;
        bool _outlineDirty = true;
        private RenderEffects _selectedEffect;
        private RenderEffects _defaultEffect;
        private DisplayStyle _effectStyle;

        private readonly object _renderMutex;

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

        private void InitEffects(DisplayStyle displayStyle)
        {
            if (displayStyle == _effectStyle && _selectedEffect.IsValid && _defaultEffect.IsValid)
                return;
            _effectStyle = displayStyle;
            _selectedEffect?.Dispose();
            _defaultEffect?.Dispose();

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

            _defaultEffect = new RenderEffects { EdgeEffect = new EffectInstance(VertexFormatBits.Position), MeshEffect = new EffectInstance(format) };
            _defaultEffect.EdgeEffect.SetColor(new Color(150, 150, 150));
            _defaultEffect.EdgeEffect.SetTransparency(0.4);

            _defaultEffect.MeshEffect.SetColor(new Color(150, 150, 150));
            _defaultEffect.MeshEffect.SetTransparency(0.4);
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
                        item.Render(xform, _defaultEffect);
                    }
                }
            }
        }

        public bool UseInTransparentPass(View dBView) => true;

        public bool UsesHandles() => false;

        internal static PreviewServer StartNewServer(BufferPools pools)
        {
            ExternalServiceId serviceId = ExternalServices.BuiltInExternalServices.DirectContext3DService;
            var directContext3dService = ExternalServiceRegistry.GetService(serviceId) as MultiServerService;

            var doc = DocumentManager.Instance.CurrentDBDocument;
            Debug.WriteLine($"doc null? {doc == null} {doc?.Title} ");
            IList<Guid> activeList = directContext3dService.GetActiveServerIds();

            var previewServer = new PreviewServer(pools);
            Debug.WriteLine($"Adding server {previewServer.GetServerId()} ...");
            activeServers.Add(previewServer.GetServerId(), previewServer);
            activeList.Add(previewServer.GetServerId());
            directContext3dService.AddServer(previewServer);
            directContext3dService.SetActiveServers(activeList);
            Debug.WriteLine($"Server was added");

            return previewServer;
        }

        public void StopServer()
        {
            Dispose();
        }

        internal void WithNodeCache(Guid nodeGuid, Action<NodePreviewObject> action)
        {
            _outlineDirty = true;
            lock(_renderMutex)
            {
                NodePreviewObject cached;
                if (!_previewObjects.TryGetValue(nodeGuid, out cached))
                {
                    cached = new NodePreviewObject(nodeGuid);
                    _previewObjects[nodeGuid] = cached;
                }
                action(cached);
            }
            _outlineDirty = true;
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

            _selectedEffect?.Dispose();
            _defaultEffect?.Dispose();

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

    internal class RenderEffects : IDisposable
    {
        public required EffectInstance EdgeEffect { get; init; }
        public required EffectInstance MeshEffect { get; init; }
        public bool IsValid => EdgeEffect != null && EdgeEffect.IsValid() && MeshEffect != null && MeshEffect.IsValid();

        public void Dispose()
        {
            EdgeEffect?.Dispose();
            MeshEffect?.Dispose();
        }
    }

    internal class NodePreviewObject : IDisposable
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
            // Reusing this instance after dispose is fine, but wrap it for clarity
            Dispose();
        }

        public void AddMesh(IRenderPackage meshRenderPackage)
        {
            _meshCache ??= new BufferCache(PrimitiveType.TriangleList);
            _meshCache.FromMeshRenderPackage(meshRenderPackage);
            OutlineWrapper.UpdateOrCreateNew(ref _outline, _meshCache.Outline);
        }

        public void AddEdge(IRenderPackage edgeRenderPackage)
        {
            _edgeCache ??= new BufferCache(PrimitiveType.LineList);
            _edgeCache.FromLineRenderPackage(edgeRenderPackage);
            OutlineWrapper.UpdateOrCreateNew(ref _outline, _edgeCache.Outline);
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
    }
    
    internal class BufferCache : IDisposable
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
            var capacity = buffer.Capacity;
            if (bufCount > 1)
            {
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
        }

        public BufferedProtoPreview FromMeshRenderPackage(IRenderPackage pkg)
        {
            if (double.IsNaN(_modelScale))
                _modelScale = Revit.GeometryConversion.UnitConverter.DynamoToHostFactor(SpecTypeId.Length);

            var max = ushort.MaxValue + 1;
            var vertexComponents = pkg.MeshVertices.ToArray();
            var normalComponents = pkg.MeshNormals.ToArray();
            var numVertices = vertexComponents.Length / 3;
            var numTris = numVertices / 3;
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

            var indexBuffer = new int[max - 2]; // can't use the full 65k buffer for some reason (the last line is removed)
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

    internal class BufferedProtoPreview : IDisposable
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

        private VertexBuffer _vertexBuffer;
        private IndexBuffer _indexBuffer;
        private VertexFormat _vertexFormat;

        //private BufferPools _pools;

        private Outline _outline;

        private readonly PrimitiveType _type;
        private readonly int _vertexSize;
        private readonly int _indexSize;

        public int Capacity => (MAX_SIZE * _vertexSize) - _vertexComponentCount;
        public int PrimitiveCount => _indexComponentCount / _indexSize;

        public bool HasValidBuffers => _vertexBuffer != null && _vertexBuffer.IsValid()
            && _indexBuffer != null && _indexBuffer.IsValid()
            && _vertexFormat != null && _vertexFormat.IsValid();

        public Outline Outline
        {
            get
            {
                if (_min.Item1 > _max.Item1)
                    return null; // this outline is invalid
                var pMin = new XYZ(_min.Item1, _min.Item2, _min.Item3);
                var pMax = new XYZ(_max.Item1, _max.Item2, _max.Item3);
                OutlineWrapper.UpdateOrCreateNew(ref _outline, pMin, pMax);
                return _outline;
            }
        }

        internal BufferedProtoPreview(PrimitiveType primitiveType)
        {
            //_pools = pools;
            _type = primitiveType;
            (_vertexSize, _indexSize) = _type switch
            {
                PrimitiveType.TriangleList => (6, 3),
                PrimitiveType.LineList => (3, 2),
                _ => throw new NotImplementedException()
            };

            // unsure if the max size is the size of the backing byte buffer, or the amount of floats
            _vertexComponents = new float[MAX_SIZE * _vertexSize];
            _indices = new byte[MAX_SIZE * _indexSize * 2]; 
        }

        private void InitializeRenderBuffers()
        {
            _vertexBuffer = new VertexBuffer(MAX_SIZE * _vertexSize);
            _indexBuffer = new IndexBuffer(MAX_SIZE * _indexSize);

            _vertexFormat = _type switch
            {
                PrimitiveType.TriangleList => new VertexFormat(VertexFormatBits.PositionNormal),
                PrimitiveType.LineList => new VertexFormat(VertexFormatBits.Position),
                _ => throw new NotImplementedException()
            };
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (double x, double y, double z) GetHelixComponents(in ReadOnlySpan<double> components, int idx, double scale)
        {
            var x = components[idx++] * scale;
            var z = components[idx++] * scale;
            var y = -components[idx] * scale;
            return (x, y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static (double x, double y, double z) GetComponents(in ReadOnlySpan<double> components, int idx)
        {
            var x = components[idx++];
            var y = components[idx++];
            var z = components[idx];
            return (x, y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetComponents(double x, double y, double z)
        {
            _vertexComponents[_vertexComponentCount++] = (float)x;
            _vertexComponents[_vertexComponentCount++] = (float)y;
            _vertexComponents[_vertexComponentCount++] = (float)z;
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
                var (x, y, z) = GetHelixComponents(in vertexComponents, i * 3, scale);
                //var j = i * 3;
                //var x = vertexComponents[j++] * scale;
                ////var y = vertexComponents[j++] * scale;
                ////var z = vertexComponents[j] * scale;
                //var z = vertexComponents[j++] * scale;
                //var y = -vertexComponents[j] * scale;
                xMin = Math.Min(xMin, x);
                yMin = Math.Min(yMin, y);
                zMin = Math.Min(zMin, z);
                xMax = Math.Max(xMax, x);
                yMax = Math.Max(yMax, y);
                zMax = Math.Max(zMax, z);
                SetComponents(x, y, z);
                
                //j = i * 3; // restart the component index for the normals
                //var nx = (float)normalComponents[j++];
                ////var ny = (float)normalComponents[j++];
                ////var nz = (float)normalComponents[j];
                //var nz = (float)normalComponents[j++];
                //var ny = -(float)normalComponents[j];
                var (nx, ny, nz) = GetHelixComponents(in normalComponents, i * 3, 1);
                SetComponents(nx, ny, nz);
                //_vertexComponents[_vertexComponentCount++] = nx;
                //_vertexComponents[_vertexComponentCount++] = ny;
                //_vertexComponents[_vertexComponentCount++] = nz;
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
                var (x, y, z) = GetHelixComponents(in vertexComponents, i * 3, scale);
                //var j = i * 3;
                //var x = vertexComponents[j++] * scale;
                ////var y = vertexComponents[j++] * scale;
                ////var z = vertexComponents[j] * scale;
                //var z = vertexComponents[j++] * scale; 
                //var y = -vertexComponents[j] * scale;
                xMin = Math.Min(xMin, x);
                yMin = Math.Min(yMin, y);
                zMin = Math.Min(zMin, z);
                xMax = Math.Max(xMax, x);
                yMax = Math.Max(yMax, y);
                zMax = Math.Max(zMax, z);
                SetComponents(x, y, z);
                //_vertexComponents[_vertexComponentCount++] = (float)x;
                //_vertexComponents[_vertexComponentCount++] = (float)y;
                //_vertexComponents[_vertexComponentCount++] = (float)z;
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
            DestroyRenderBuffers();
        }

        private void DestroyRenderBuffers()
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

        public void Render(Transform transform, EffectInstance effectInstance)
        {
            if (!HasValidBuffers)
                DestroyRenderBuffers(); // the old buffers have been invalidated, remove them

            if (!_initialized)
                InitializeRenderBuffers(); // ensure we have buffers to render

            if (!_synced)
                CopyBuffers(); // ensure the buffer content is up to date

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

    internal static class OutlineWrapper
    {
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

    #region reusable buffers
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
    #endregion
}
