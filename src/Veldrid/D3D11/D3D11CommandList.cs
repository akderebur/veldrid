﻿using System;
using SharpDX.Direct3D11;
using SharpDX.Mathematics.Interop;
using System.Diagnostics;

namespace Veldrid.D3D11
{
    internal class D3D11CommandList : CommandList
    {
        private readonly D3D11GraphicsDevice _gd;
        private readonly DeviceContext _context;
        private readonly DeviceContext1 _context1;
        private bool _begun;

        private RawViewportF[] _viewports = new RawViewportF[0];
        private RawRectangle[] _scissors = new RawRectangle[0];
        private bool _viewportsChanged;
        private bool _scissorRectsChanged;

        private uint _numVertexBindings = 0;
        private SharpDX.Direct3D11.Buffer[] _vertexBindings = new SharpDX.Direct3D11.Buffer[1];
        private int[] _vertexStrides;
        private int[] _vertexOffsets = new int[1];

        // Cached pipeline State
        private D3D11Pipeline _graphicsPipeline;
        private Buffer _ib;
        private BlendState _blendState;
        private DepthStencilState _depthStencilState;
        private RasterizerState _rasterizerState;
        private SharpDX.Direct3D.PrimitiveTopology _primitiveTopology;
        private InputLayout _inputLayout;
        private VertexShader _vertexShader;
        private GeometryShader _geometryShader;
        private HullShader _hullShader;
        private DomainShader _domainShader;
        private PixelShader _pixelShader;

        private D3D11ResourceSet[] _graphicsResourceSets = new D3D11ResourceSet[1];

        private D3D11Pipeline _computePipeline;
        private D3D11ResourceSet[] _computeResourceSets = new D3D11ResourceSet[1];

        // Cached resources
        private const int MaxCachedUniformBuffers = 15;
        private readonly D3D11Buffer[] _vertexBoundUniformBuffers = new D3D11Buffer[MaxCachedUniformBuffers];
        private readonly D3D11Buffer[] _fragmentBoundUniformBuffers = new D3D11Buffer[MaxCachedUniformBuffers];
        private const int MaxCachedTextureViews = 16;
        private readonly D3D11TextureView[] _vertexBoundTextureViews = new D3D11TextureView[MaxCachedTextureViews];
        private readonly D3D11TextureView[] _fragmentBoundTextureViews = new D3D11TextureView[MaxCachedTextureViews];
        private const int MaxCachedSamplers = 4;
        private readonly D3D11Sampler[] _vertexBoundSamplers = new D3D11Sampler[MaxCachedSamplers];
        private readonly D3D11Sampler[] _fragmentBoundSamplers = new D3D11Sampler[MaxCachedSamplers];

        public D3D11CommandList(D3D11GraphicsDevice gd, ref CommandListDescription description)
            : base(ref description)
        {
            _gd = gd;
            _context = new DeviceContext(gd.Device);
            _context1 = _context.QueryInterfaceOrNull<DeviceContext1>();
            if (_context1 == null)
            {
                throw new VeldridException("Direct3D 11.1 is required.");
            }
        }

        public SharpDX.Direct3D11.CommandList DeviceCommandList { get; set; }

        internal DeviceContext DeviceContext => _context;

        private D3D11Framebuffer D3D11Framebuffer => Util.AssertSubtype<Framebuffer, D3D11Framebuffer>(_framebuffer);

        public override void Begin()
        {
            DeviceCommandList?.Dispose();
            DeviceCommandList = null;
            ClearState();
            _begun = true;
        }

        private void ClearState()
        {
            _context.ClearState();
            ResetManagedState();
        }

        private void ResetManagedState()
        {
            _numVertexBindings = 0;
            Util.ClearArray(_vertexBindings);
            _vertexStrides = null;
            Util.ClearArray(_vertexOffsets);

            _framebuffer = null;

            Util.ClearArray(_viewports);
            Util.ClearArray(_scissors);
            _viewportsChanged = false;
            _scissorRectsChanged = false;

            _ib = null;
            _graphicsPipeline = null;
            _blendState = null;
            _depthStencilState = null;
            _rasterizerState = null;
            _primitiveTopology = SharpDX.Direct3D.PrimitiveTopology.Undefined;
            _inputLayout = null;
            _vertexShader = null;
            _geometryShader = null;
            _hullShader = null;
            _domainShader = null;
            _pixelShader = null;
            Util.ClearArray(_graphicsResourceSets);

            Util.ClearArray(_vertexBoundUniformBuffers);
            Util.ClearArray(_vertexBoundTextureViews);
            Util.ClearArray(_vertexBoundSamplers);

            Util.ClearArray(_fragmentBoundUniformBuffers);
            Util.ClearArray(_fragmentBoundTextureViews);
            Util.ClearArray(_fragmentBoundSamplers);

            _computePipeline = null;
            Util.ClearArray(_computeResourceSets);
        }

        public override void End()
        {
            if (DeviceCommandList != null)
            {
                throw new VeldridException("Invalid use of End().");
            }

            DeviceCommandList = _context.FinishCommandList(false);
            ResetManagedState();
            _begun = false;
        }

        public void Reset()
        {
            if (DeviceCommandList != null)
            {
                DeviceCommandList.Dispose();
                DeviceCommandList = null;
            }
            else if (_begun)
            {
                _context.ClearState();
                SharpDX.Direct3D11.CommandList cl = _context.FinishCommandList(false);
                cl.Dispose();
            }

            ResetManagedState();
        }

        protected override void SetIndexBufferCore(Buffer buffer, IndexFormat format)
        {
            if (_ib != buffer)
            {
                _ib = buffer;
                D3D11Buffer d3d11Buffer = Util.AssertSubtype<Buffer, D3D11Buffer>(buffer);
                _context.InputAssembler.SetIndexBuffer(d3d11Buffer.Buffer, D3D11Formats.ToDxgiFormat(format), 0);
            }
        }

        public override void SetPipeline(Pipeline pipeline)
        {
            if (!pipeline.IsComputePipeline && _graphicsPipeline != pipeline)
            {
                D3D11Pipeline d3dPipeline = Util.AssertSubtype<Pipeline, D3D11Pipeline>(pipeline);
                _graphicsPipeline = d3dPipeline;
                Util.ClearArray(_graphicsResourceSets); // Invalidate resource set bindings -- they may be invalid.

                BlendState blendState = d3dPipeline.BlendState;
                if (_blendState != blendState)
                {
                    _blendState = blendState;
                    _context.OutputMerger.SetBlendState(blendState);
                }

                DepthStencilState depthStencilState = d3dPipeline.DepthStencilState;
                if (_depthStencilState != depthStencilState)
                {
                    _depthStencilState = depthStencilState;
                    _context.OutputMerger.SetDepthStencilState(depthStencilState);
                }

                RasterizerState rasterizerState = d3dPipeline.RasterizerState;
                if (_rasterizerState != rasterizerState)
                {
                    _rasterizerState = rasterizerState;
                    _context.Rasterizer.State = rasterizerState;
                }

                SharpDX.Direct3D.PrimitiveTopology primitiveTopology = d3dPipeline.PrimitiveTopology;
                if (_primitiveTopology != primitiveTopology)
                {
                    _primitiveTopology = primitiveTopology;
                    _context.InputAssembler.PrimitiveTopology = primitiveTopology;
                }

                InputLayout inputLayout = d3dPipeline.InputLayout;
                if (_inputLayout != inputLayout)
                {
                    _inputLayout = inputLayout;
                    _context.InputAssembler.InputLayout = inputLayout;
                }

                VertexShader vertexShader = d3dPipeline.VertexShader;
                if (_vertexShader != vertexShader)
                {
                    _vertexShader = vertexShader;
                    _context.VertexShader.Set(vertexShader);
                }

                GeometryShader geometryShader = d3dPipeline.GeometryShader;
                if (_geometryShader != geometryShader)
                {
                    _geometryShader = geometryShader;
                    _context.GeometryShader.Set(geometryShader);
                }

                HullShader hullShader = d3dPipeline.HullShader;
                if (_hullShader != hullShader)
                {
                    _hullShader = hullShader;
                    _context.HullShader.Set(hullShader);
                }

                DomainShader domainShader = d3dPipeline.DomainShader;
                if (_domainShader != domainShader)
                {
                    _domainShader = domainShader;
                    _context.DomainShader.Set(domainShader);
                }

                PixelShader pixelShader = d3dPipeline.PixelShader;
                if (_pixelShader != pixelShader)
                {
                    _pixelShader = pixelShader;
                    _context.PixelShader.Set(pixelShader);
                }

                _vertexStrides = d3dPipeline.VertexStrides;
                if (_vertexStrides != null)
                {
                    int vertexStridesCount = _vertexStrides.Length;
                    Util.EnsureArraySize(ref _vertexBindings, (uint)vertexStridesCount);
                    Util.EnsureArraySize(ref _vertexOffsets, (uint)vertexStridesCount);
                }

                Util.EnsureArraySize(ref _graphicsResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
            }
            else if (pipeline.IsComputePipeline && _computePipeline != pipeline)
            {
                D3D11Pipeline d3dPipeline = Util.AssertSubtype<Pipeline, D3D11Pipeline>(pipeline);
                _computePipeline = d3dPipeline;
                Util.ClearArray(_computeResourceSets); // Invalidate resource set bindings -- they may be invalid.

                ComputeShader computeShader = d3dPipeline.ComputeShader;
                _context.ComputeShader.Set(computeShader);
                Util.EnsureArraySize(ref _computeResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
            }
        }

        protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs)
        {
            if (_graphicsResourceSets[slot] == rs)
            {
                return;
            }

            D3D11ResourceSet d3d11RS = Util.AssertSubtype<ResourceSet, D3D11ResourceSet>(rs);
            _graphicsResourceSets[slot] = d3d11RS;
            int cbBase = GetConstantBufferBase(slot, true);
            int uaBase = GetUnorderedAccessBase(slot, true);
            int textureBase = GetTextureBase(slot, true);
            int samplerBase = GetSamplerBase(slot, true);

            D3D11ResourceLayout layout = d3d11RS.Layout;
            BindableResource[] resources = d3d11RS.Resources;
            for (int i = 0; i < resources.Length; i++)
            {
                BindableResource resource = resources[i];
                D3D11ResourceLayout.ResourceBindingInfo rbi = layout.GetDeviceSlotIndex(i);
                switch (rbi.Kind)
                {
                    case ResourceKind.UniformBuffer:
                        D3D11Buffer uniformBuffer = Util.AssertSubtype<BindableResource, D3D11Buffer>(resource);
                        BindUniformBuffer(uniformBuffer, cbBase + rbi.Slot, rbi.Stages);
                        break;
                    case ResourceKind.StructuredBufferReadOnly:
                        D3D11Buffer storageBufferRO = Util.AssertSubtype<BindableResource, D3D11Buffer>(resource);
                        BindStorageBufferView(storageBufferRO, textureBase + rbi.Slot, rbi.Stages);
                        break;
                    case ResourceKind.StructuredBufferReadWrite:
                        D3D11Buffer storageBuffer = Util.AssertSubtype<BindableResource, D3D11Buffer>(resource);
                        BindUnorderedAccessBuffer(storageBuffer, uaBase + rbi.Slot, rbi.Stages);
                        break;
                    case ResourceKind.TextureReadOnly:
                        D3D11TextureView texView = Util.AssertSubtype<BindableResource, D3D11TextureView>(resource);
                        BindTextureView(texView, textureBase + rbi.Slot, rbi.Stages);
                        break;
                    case ResourceKind.TextureReadWrite:
                        // TODO: Implement read-write textures.
                        throw new NotImplementedException();
                    case ResourceKind.Sampler:
                        D3D11Sampler sampler = Util.AssertSubtype<BindableResource, D3D11Sampler>(resource);
                        BindSampler(sampler, samplerBase + rbi.Slot, rbi.Stages);
                        break;
                    default: throw Illegal.Value<ResourceKind>();
                }
            }
        }

        protected override void SetComputeResourceSetCore(uint slot, ResourceSet set)
        {
            if (_computeResourceSets[slot] == set)
            {
                return;
            }

            D3D11ResourceSet d3d11RS = Util.AssertSubtype<ResourceSet, D3D11ResourceSet>(set);
            _computeResourceSets[slot] = d3d11RS;
            int cbBase = GetConstantBufferBase(slot, false);
            int uaBase = GetUnorderedAccessBase(slot, false);
            int textureBase = GetTextureBase(slot, false);
            int samplerBase = GetSamplerBase(slot, false);

            D3D11ResourceLayout layout = d3d11RS.Layout;
            BindableResource[] resources = d3d11RS.Resources;
            for (int i = 0; i < resources.Length; i++)
            {
                BindableResource resource = resources[i];
                D3D11ResourceLayout.ResourceBindingInfo rbi = layout.GetDeviceSlotIndex(i);
                Debug.Assert(rbi.Stages == ShaderStages.Compute);
                switch (rbi.Kind)
                {
                    case ResourceKind.UniformBuffer:
                        D3D11Buffer uniformBuffer = Util.AssertSubtype<BindableResource, D3D11Buffer>(resource);
                        BindUniformBuffer(uniformBuffer, cbBase + rbi.Slot, rbi.Stages);
                        break;
                    case ResourceKind.StructuredBufferReadOnly:
                        D3D11Buffer storageBufferRO = Util.AssertSubtype<BindableResource, D3D11Buffer>(resource);
                        BindStorageBufferView(storageBufferRO, textureBase + rbi.Slot, rbi.Stages);
                        break;
                    case ResourceKind.StructuredBufferReadWrite:
                        D3D11Buffer storageBuffer = Util.AssertSubtype<BindableResource, D3D11Buffer>(resource);
                        BindUnorderedAccessBuffer(storageBuffer, uaBase + rbi.Slot, rbi.Stages);
                        break;
                    case ResourceKind.TextureReadOnly:
                        D3D11TextureView texView = Util.AssertSubtype<BindableResource, D3D11TextureView>(resource);
                        BindTextureView(texView, textureBase + rbi.Slot, rbi.Stages);
                        break;
                    case ResourceKind.TextureReadWrite:
                        // TODO: Implement read-write textures.
                        // Store a UAV in the D3D11TextureView if texture is created with Storage flag.
                        throw new NotImplementedException();
                    case ResourceKind.Sampler:
                        D3D11Sampler sampler = Util.AssertSubtype<BindableResource, D3D11Sampler>(resource);
                        BindSampler(sampler, samplerBase + rbi.Slot, rbi.Stages);
                        break;
                    default: throw Illegal.Value<ResourceKind>();
                }
            }
        }

        private int GetConstantBufferBase(uint slot, bool graphics)
        {
            D3D11ResourceLayout[] layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;
            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].UniformBufferCount;
            }

            return ret;
        }

        private int GetUnorderedAccessBase(uint slot, bool graphics)
        {
            D3D11ResourceLayout[] layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;
            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].StorageBufferCount;
            }

            return ret;
        }

        private int GetTextureBase(uint slot, bool graphics)
        {
            D3D11ResourceLayout[] layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;
            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].TextureCount;
            }

            return ret;
        }

        private int GetSamplerBase(uint slot, bool graphics)
        {
            D3D11ResourceLayout[] layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;
            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].SamplerCount;
            }

            return ret;
        }

        protected override void SetVertexBufferCore(uint index, Buffer buffer)
        {
            D3D11Buffer d3d11Buffer = Util.AssertSubtype<Buffer, D3D11Buffer>(buffer);
            _vertexBindings[index] = d3d11Buffer.Buffer;
            _numVertexBindings = Math.Max((index + 1), _numVertexBindings);
        }

        public override void Draw(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
        {
            PreDrawCommand();

            if (instanceCount == 1)
            {
                _context.Draw((int)vertexCount, (int)vertexStart);
            }
            else
            {
                _context.DrawInstanced((int)vertexCount, (int)instanceCount, (int)vertexStart, (int)instanceStart);
            }
        }

        public override void DrawIndexed(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            PreDrawCommand();

            Debug.Assert(_ib != null);
            if (instanceCount == 1)
            {
                _context.DrawIndexed((int)indexCount, (int)indexStart, vertexOffset);
            }
            else
            {
                _context.DrawIndexedInstanced((int)indexCount, (int)instanceCount, (int)indexStart, vertexOffset, (int)instanceStart);
            }
        }

        protected override void DrawIndirectCore(Buffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            PreDrawCommand();

            D3D11Buffer d3d11Buffer = Util.AssertSubtype<Buffer, D3D11Buffer>(indirectBuffer);
            int currentOffset = (int)offset;
            for (uint i = 0; i < drawCount; i++)
            {
                _context.DrawInstancedIndirect(d3d11Buffer.Buffer, currentOffset);
                currentOffset += (int)stride;
            }
        }

        protected override void DrawIndexedIndirectCore(Buffer indirectBuffer, uint offset, uint drawCount, uint stride)
        {
            PreDrawCommand();

            D3D11Buffer d3d11Buffer = Util.AssertSubtype<Buffer, D3D11Buffer>(indirectBuffer);
            int currentOffset = (int)offset;
            for (uint i = 0; i < drawCount; i++)
            {
                _context.DrawIndexedInstancedIndirect(d3d11Buffer.Buffer, currentOffset);
                currentOffset += (int)stride;
            }
        }

        public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            _context.Dispatch((int)groupCountX, (int)groupCountY, (int)groupCountZ);
        }

        protected override void DispatchIndirectCore(Buffer indirectBuffer, uint offset)
        {
            D3D11Buffer d3d11Buffer = Util.AssertSubtype<Buffer, D3D11Buffer>(indirectBuffer);
            _context.DispatchIndirect(d3d11Buffer.Buffer, (int)offset);
        }

        private void PreDrawCommand()
        {
            FlushViewports();
            FlushScissorRects();
            FlushVertexBindings();
        }

        protected override void ResolveTextureCore(Texture source, Texture destination)
        {
            D3D11Texture d3d11Source = Util.AssertSubtype<Texture, D3D11Texture>(source);
            D3D11Texture d3d11Destination = Util.AssertSubtype<Texture, D3D11Texture>(destination);
            _context.ResolveSubresource(
                d3d11Source.DeviceTexture,
                0,
                d3d11Destination.DeviceTexture,
                0,
                d3d11Destination.DxgiFormat);
        }

        private void FlushViewports()
        {
            if (_viewportsChanged)
            {
                _viewportsChanged = false;
                _context.Rasterizer.SetViewports(_viewports, _viewports.Length);
            }
        }

        private void FlushScissorRects()
        {
            if (_scissorRectsChanged)
            {
                _scissorRectsChanged = false;
                if (_scissors.Length > 0)
                {
                    _context.Rasterizer.SetScissorRectangles(_scissors);
                }
            }
        }

        private unsafe void FlushVertexBindings()
        {
            IntPtr* buffersPtr = stackalloc IntPtr[(int)_numVertexBindings];
            for (int i = 0; i < _numVertexBindings; i++)
            {
                buffersPtr[i] = _vertexBindings[i].NativePointer;
            }
            fixed (int* stridesPtr = _vertexStrides)
            fixed (int* offsetsPtr = _vertexOffsets)
            {
                _context.InputAssembler.SetVertexBuffers(0, (int)_numVertexBindings, (IntPtr)buffersPtr, (IntPtr)stridesPtr, (IntPtr)offsetsPtr);
            }
        }

        public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
        {
            _scissorRectsChanged = true;
            Util.EnsureArraySize(ref _scissors, index + 1);
            _scissors[index] = new RawRectangle((int)x, (int)y, (int)(x + width), (int)(y + height));
        }

        public override void SetViewport(uint index, ref Viewport viewport)
        {
            _viewportsChanged = true;
            Util.EnsureArraySize(ref _viewports, index + 1);
            _viewports[index] = new RawViewportF
            {
                X = viewport.X,
                Y = viewport.Y,
                Width = viewport.Width,
                Height = viewport.Height,
                MinDepth = viewport.MinDepth,
                MaxDepth = viewport.MaxDepth
            };
        }

        private void BindTextureView(D3D11TextureView texView, int slot, ShaderStages stages)
        {
            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
            {
                bool bind = false;
                if (slot < MaxCachedUniformBuffers)
                {
                    if (_vertexBoundTextureViews[slot] != texView)
                    {
                        _vertexBoundTextureViews[slot] = texView;
                        bind = true;
                    }
                }
                else
                {
                    bind = true;
                }
                if (bind)
                {
                    _context.VertexShader.SetShaderResource(slot, texView.ShaderResourceView);
                }
            }
            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
            {
                _context.GeometryShader.SetShaderResource(slot, texView.ShaderResourceView);
            }
            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
            {
                _context.HullShader.SetShaderResource(slot, texView.ShaderResourceView);
            }
            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
            {
                _context.DomainShader.SetShaderResource(slot, texView.ShaderResourceView);
            }
            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
            {
                bool bind = false;
                if (slot < MaxCachedUniformBuffers)
                {
                    if (_fragmentBoundTextureViews[slot] != texView)
                    {
                        _fragmentBoundTextureViews[slot] = texView;
                        bind = true;
                    }
                }
                else
                {
                    bind = true;
                }
                if (bind)
                {
                    _context.PixelShader.SetShaderResource(slot, texView.ShaderResourceView);
                }
            }
            if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
            {
                _context.ComputeShader.SetShaderResource(slot, texView.ShaderResourceView);
            }
        }

        private void BindStorageBufferView(D3D11Buffer storageBufferRO, int slot, ShaderStages stages)
        {
            _context.ComputeShader.SetUnorderedAccessView(0, null);

            ShaderResourceView srv = storageBufferRO.ShaderResourceView;
            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
            {
                _context.VertexShader.SetShaderResource(slot, srv);
            }
            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
            {
                _context.GeometryShader.SetShaderResource(slot, srv);
            }
            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
            {
                _context.HullShader.SetShaderResource(slot, srv);
            }
            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
            {
                _context.DomainShader.SetShaderResource(slot, srv);
            }
            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
            {
                _context.PixelShader.SetShaderResource(slot, srv);
            }
            if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
            {
                _context.ComputeShader.SetShaderResource(slot, srv);
            }
        }

        private void BindUniformBuffer(D3D11Buffer ub, int slot, ShaderStages stages)
        {
            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
            {
                bool bind = false;
                if (slot < MaxCachedUniformBuffers)
                {
                    if (_vertexBoundUniformBuffers[slot] != ub)
                    {
                        _vertexBoundUniformBuffers[slot] = ub;
                        bind = true;
                    }
                }
                else
                {
                    bind = true;
                }
                if (bind)
                {
                    _context.VertexShader.SetConstantBuffer(slot, ub.Buffer);
                }
            }
            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
            {
                _context.GeometryShader.SetConstantBuffer(slot, ub.Buffer);
            }
            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
            {
                _context.HullShader.SetConstantBuffer(slot, ub.Buffer);
            }
            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
            {
                _context.DomainShader.SetConstantBuffer(slot, ub.Buffer);
            }
            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
            {
                bool bind = false;
                if (slot < MaxCachedUniformBuffers)
                {
                    if (_fragmentBoundUniformBuffers[slot] != ub)
                    {
                        _fragmentBoundUniformBuffers[slot] = ub;
                        bind = true;
                    }
                }
                else
                {
                    bind = true;
                }
                if (bind)
                {
                    _context.PixelShader.SetConstantBuffer(slot, ub.Buffer);
                }
            }
            if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
            {
                _context.ComputeShader.SetConstantBuffer(slot, ub.Buffer);
            }
        }

        private void BindUnorderedAccessBuffer(D3D11Buffer storageBuffer, int slot, ShaderStages stages)
        {
            Debug.Assert(stages == ShaderStages.Compute || ((stages & ShaderStages.Compute) == 0));

            UnorderedAccessView uav = storageBuffer.UnorderedAccessView;
            int baseSlot = 0;
            if (stages != ShaderStages.Compute && _framebuffer != null)
            {
                baseSlot = _framebuffer.ColorTargets.Count;
            }

            if (stages == ShaderStages.Compute)
            {
                _context.ComputeShader.SetUnorderedAccessView(baseSlot + slot, uav);
            }
            else
            {
                _context.OutputMerger.SetUnorderedAccessView(baseSlot + slot, uav);
            }
        }

        private void BindSampler(D3D11Sampler sampler, int slot, ShaderStages stages)
        {
            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
            {
                bool bind = false;
                if (slot < MaxCachedSamplers)
                {
                    if (_vertexBoundSamplers[slot] != sampler)
                    {
                        _vertexBoundSamplers[slot] = sampler;
                        bind = true;
                    }
                }
                else
                {
                    bind = true;
                }
                if (bind)
                {
                    _context.VertexShader.SetSampler(slot, sampler.DeviceSampler);
                }
            }
            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
            {
                _context.GeometryShader.SetSampler(slot, sampler.DeviceSampler);
            }
            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
            {
                _context.HullShader.SetSampler(slot, sampler.DeviceSampler);
            }
            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
            {
                _context.DomainShader.SetSampler(slot, sampler.DeviceSampler);
            }
            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
            {
                bool bind = false;
                if (slot < MaxCachedSamplers)
                {
                    if (_fragmentBoundSamplers[slot] != sampler)
                    {
                        _fragmentBoundSamplers[slot] = sampler;
                        bind = true;
                    }
                }
                else
                {
                    bind = true;
                }
                if (bind)
                {
                    _context.PixelShader.SetSampler(slot, sampler.DeviceSampler);
                }
            }
        }

        protected override void SetFramebufferCore(Framebuffer fb)
        {
            D3D11Framebuffer d3dFB = Util.AssertSubtype<Framebuffer, D3D11Framebuffer>(fb);
            if (d3dFB.IsSwapchainFramebuffer)
            {
                _gd.CommandListsReferencingSwapchain.Add(this);
            }

            _context.OutputMerger.SetRenderTargets(d3dFB.DepthStencilView, d3dFB.RenderTargetViews);
        }

        public override void ClearColorTarget(uint index, RgbaFloat clearColor)
        {
            _context.ClearRenderTargetView(D3D11Framebuffer.RenderTargetViews[index], new RawColor4(clearColor.R, clearColor.G, clearColor.B, clearColor.A));
        }

        public override void ClearDepthTarget(float depth)
        {
            _context.ClearDepthStencilView(D3D11Framebuffer.DepthStencilView, DepthStencilClearFlags.Depth, depth, 0);
        }

        public override void UpdateBuffer(Buffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
        {
            D3D11Buffer d3dBuffer = Util.AssertSubtype<Buffer, D3D11Buffer>(buffer);
            if (sizeInBytes == 0)
            {
                return;
            }

            ResourceRegion? subregion = null;
            if ((d3dBuffer.Buffer.Description.BindFlags & BindFlags.ConstantBuffer) != BindFlags.ConstantBuffer)
            {
                // For a shader-constant buffer; set pDstBox to null. It is not possible to use
                // this method to partially update a shader-constant buffer

                subregion = new ResourceRegion()
                {
                    Left = (int)bufferOffsetInBytes,
                    Right = (int)(sizeInBytes + bufferOffsetInBytes),
                    Bottom = 1,
                    Back = 1
                };
            }

            if (bufferOffsetInBytes == 0)
            {
                _context.UpdateSubresource(d3dBuffer.Buffer, 0, subregion, source, 0, 0);
            }
            else
            {
                _context1.UpdateSubresource1(d3dBuffer.Buffer, 0, subregion, source, 0, 0, 0);
            }
        }

        public override void UpdateTexture(
            Texture texture,
            IntPtr source,
            uint sizeInBytes,
            uint x,
            uint y,
            uint z,
            uint width,
            uint height,
            uint depth,
            uint mipLevel,
            uint arrayLayer)
        {
            Texture2D deviceTexture = Util.AssertSubtype<Texture, D3D11Texture>(texture).DeviceTexture;
            ResourceRegion resourceRegion = new ResourceRegion(
                left: (int)x,
                top: (int)y,
                front: (int)z,
                right: (int)(x + width),
                bottom: (int)(y + height),
                back: (int)(z + depth));
            uint srcRowPitch = FormatHelpers.GetSizeInBytes(texture.Format) * width;
            _context.UpdateSubresource(deviceTexture, (int)mipLevel, resourceRegion, source, (int)srcRowPitch, 0);
        }

        public override void UpdateTextureCube(
            Texture textureCube,
            IntPtr source,
            uint sizeInBytes,
            CubeFace face,
            uint x,
            uint y,
            uint width,
            uint height,
            uint mipLevel,
            uint arrayLayer)
        {
            Texture2D deviceTexture = Util.AssertSubtype<Texture, D3D11Texture>(textureCube).DeviceTexture;

            ResourceRegion resourceRegion = new ResourceRegion(
                left: (int)x,
                right: (int)x + (int)width,
                top: (int)y,
                bottom: (int)y + (int)height,
                front: 0,
                back: 1);
            uint srcRowPitch = FormatHelpers.GetSizeInBytes(textureCube.Format) * width;
            int subresource = GetSubresource(face, mipLevel, textureCube.MipLevels);
            _context.UpdateSubresource(deviceTexture, subresource, resourceRegion, source, (int)srcRowPitch, 0);
        }

        private int GetSubresource(CubeFace face, uint level, uint totalLevels)
        {
            int faceOffset;
            switch (face)
            {
                case CubeFace.NegativeX:
                    faceOffset = 1;
                    break;
                case CubeFace.PositiveX:
                    faceOffset = 0;
                    break;
                case CubeFace.NegativeY:
                    faceOffset = 3;
                    break;
                case CubeFace.PositiveY:
                    faceOffset = 2;
                    break;
                case CubeFace.NegativeZ:
                    faceOffset = 4;
                    break;
                case CubeFace.PositiveZ:
                    faceOffset = 5;
                    break;
                default:
                    throw Illegal.Value<CubeFace>();
            }

            return faceOffset * (int)totalLevels + (int)level;
        }

        public override void Dispose()
        {
            DeviceCommandList?.Dispose();
            _context.Dispose();
        }
    }
}