﻿
using System.Text;

using WebGPU;
using static WebGPU.WebGPU;

namespace Ion.Extensions.Graphics;

public unsafe static class WGPUExtensions
{
	public static WGPUCommandEncoder CreateCommandEncoder(this WGPUDevice device, string label)
	{
		return wgpuDeviceCreateCommandEncoder(device, label, null);
	}

	public static void PushDebugGroup(this WGPUCommandEncoder encoder, string label)
	{
		fixed (sbyte* pLabel = label.GetUtf8Span())
		{
			wgpuCommandEncoderPushDebugGroup(encoder, pLabel);
		}
	}

	public static void PopDebugGroup(this WGPUCommandEncoder encoder)
	{
		wgpuCommandEncoderPopDebugGroup(encoder);
	}

	public static WGPUPipelineLayout CreatePipelineLayout(this WGPUDevice device)
	{
		return CreatePipelineLayout(device, new WGPUPipelineLayoutDescriptor());
	}

	public static WGPUPipelineLayout CreatePipelineLayout(this WGPUDevice device, WGPUBindGroupLayoutEntry[] bindGroupLayouts)
	{
		fixed(WGPUBindGroupLayoutEntry* pBindGroupLayouts = &bindGroupLayouts[0])
		{
			var bindGroupLayoutDesc = new WGPUBindGroupLayoutDescriptor
			{
				entries = pBindGroupLayouts,
				entryCount = (uint)bindGroupLayouts.Length
			};

			var bindGroupLayout = wgpuDeviceCreateBindGroupLayout(device, &bindGroupLayoutDesc);

			var pipelineLayoutDesc = new WGPUPipelineLayoutDescriptor
			{
				bindGroupLayouts = &bindGroupLayout,
				bindGroupLayoutCount = 1
			};

			return CreatePipelineLayout(device, pipelineLayoutDesc);
		}
	}

	public static WGPUPipelineLayout CreatePipelineLayout(this WGPUDevice device, WGPUPipelineLayoutDescriptor desc)
	{
		return wgpuDeviceCreatePipelineLayout(device, &desc);
	}

	public static WGPUBindGroupLayout CreateBindGroupLayout(this WGPUDevice device, WGPUBindGroupLayoutDescriptor desc)
	{
		return wgpuDeviceCreateBindGroupLayout(device, &desc);
	}

	public static WGPUBindGroupLayout GetBindGroupLayout(this WGPURenderPipeline renderPipeline, uint groupIndex)
	{
		return wgpuRenderPipelineGetBindGroupLayout(renderPipeline, groupIndex);
	}

	public static WGPUBindGroup CreateBindGroup(this WGPUDevice device, WGPUBindGroupLayout bindGroupLayout, params WGPUBindGroupEntry[] entries)
	{
		WGPUBindGroup bindGroup;

		fixed (WGPUBindGroupEntry* pEntries = &entries[0])
		{
			var bindGroupDescriptor = new WGPUBindGroupDescriptor
			{
				layout = bindGroupLayout,
				entries = pEntries,
				entryCount = (uint)entries.Length
			};
			bindGroup = wgpuDeviceCreateBindGroup(device, &bindGroupDescriptor);
		}

		return bindGroup;
	}

	public static WGPUShaderModule CreateShaderModule(this WGPUDevice device, string wgslShaderSource)
	{
		return wgpuDeviceCreateShaderModule(device, wgslShaderSource);
	}

	public static void Release(this WGPUShaderModule shaderModule)
	{
		wgpuShaderModuleRelease(shaderModule);
	}

	public static WGPUBuffer CreateBuffer(this WGPUDevice device, WGPUBufferUsage usage, int size, bool mappedAtCreation = false)
	{
		return wgpuDeviceCreateBuffer(device, usage, size, mappedAtCreation);
	}

	public static WGPUBuffer CreateBuffer<T>(this WGPUDevice device, WGPUQueue queue, Span<T> data, WGPUBufferUsage usage, bool mappedAtCreation = false) where T : unmanaged
	{
		return wgpuDeviceCreateBuffer<T>(device, queue, data, usage, mappedAtCreation);
	}

	public static void WriteBuffer<T>(this WGPUQueue queue, WGPUBuffer buffer, ReadOnlySpan<T> data, ulong bufferOffset = 0) where T : unmanaged
	{
		wgpuQueueWriteBuffer(queue, buffer, data, bufferOffset);
	}

	public static void WriteBuffer<T>(this WGPUQueue queue, WGPUBuffer buffer, T[] data, ulong bufferOffset = 0) where T : unmanaged
	{
		wgpuQueueWriteBuffer(queue, buffer, data, bufferOffset);
	}

	public static WGPUTextureView CreateView(this WGPUTexture texture)
	{
		return wgpuTextureCreateView(texture, null);
	}

	public static WGPUTextureView CreateView(this WGPUTexture texture, WGPUTextureViewDescriptor desc)
	{
		return wgpuTextureCreateView(texture, &desc);
	}

	public static WGPUTextureView CreateView(this WGPUTexture texture, string label, WGPUTextureViewDescriptor desc)
	{
		WGPUTextureView textureView;
		fixed (sbyte* pLabel = label.GetUtf8Span())
		{
			desc.label = pLabel;
			textureView = wgpuTextureCreateView(texture, &desc);
		}

		return textureView;
	}

	public static WGPUSampler CreateSampler(this WGPUDevice device, WGPUSamplerDescriptor descriptor)
	{
		return wgpuDeviceCreateSampler(device, &descriptor);
	}

	public static WGPURenderPipeline CreateRenderPipeline(this WGPUDevice device, WGPURenderPipelineDescriptor descriptor)
	{
		return wgpuDeviceCreateRenderPipeline(device, &descriptor);
	}

	public static WGPURenderPassEncoder BeginRenderPass(this WGPUCommandEncoder encoder, WGPURenderPassDescriptor desc)
	{
		return wgpuCommandEncoderBeginRenderPass(encoder, &desc);
	}

	public static void SetPipeline(this WGPURenderPassEncoder encoder, WGPURenderPipeline pipeline)
	{
		wgpuRenderPassEncoderSetPipeline(encoder, pipeline);
	}

	public static void SetVertexBuffer(this WGPURenderPassEncoder encoder, uint slot, WGPUBuffer buffer, ulong offset = 0, ulong size = WGPU_WHOLE_SIZE)
	{
		wgpuRenderPassEncoderSetVertexBuffer(encoder, slot, buffer, offset, size);
	}

	public static void SetIndexBuffer(this WGPURenderPassEncoder renderPassEncoder, WGPUBuffer buffer, WGPUIndexFormat format, ulong offset = 0, ulong size = WGPU_WHOLE_SIZE)
	{
		wgpuRenderPassEncoderSetIndexBuffer(renderPassEncoder, buffer, format, offset, size);
	}

	public static void SetBindGroup(this WGPURenderPassEncoder renderPassEncoder, uint groupIndex, WGPUBindGroup group, nuint dynamicOffsetCount = 0)
	{
		uint dynamicOffsets = 0;
		wgpuRenderPassEncoderSetBindGroup(renderPassEncoder, groupIndex, group, dynamicOffsetCount, &dynamicOffsets);
	}

	public static void Draw(this WGPURenderPassEncoder encoder, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
	{
		wgpuRenderPassEncoderDraw(encoder, vertexCount, instanceCount, firstVertex, firstInstance);
	}

	public static void DrawIndexed(this WGPURenderPassEncoder renderPassEncoder, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
	{
		wgpuRenderPassEncoderDrawIndexed(renderPassEncoder, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
	}

	public static void End(this WGPURenderPassEncoder encoder) => wgpuRenderPassEncoderEnd(encoder);

	public static WGPUCommandBuffer Finish(this WGPUCommandEncoder encoder, string label)
	{
		return wgpuCommandEncoderFinish(encoder, label);
	}

	public static void Submit(this WGPUQueue queue, params WGPUCommandBuffer[] commandBuffers)
	{
		wgpuQueueSubmit(queue, commandBuffers);
	}

	public static void Submit(this WGPUQueue queue, ReadOnlySpan<WGPUCommandBuffer> commandBuffers)
	{
		wgpuQueueSubmit(queue, commandBuffers);
	}

	public static void Release(this WGPUCommandEncoder encoder) => wgpuCommandEncoderRelease(encoder);

	public static void Release(this WGPUPipelineLayout pipelineLayout) => wgpuPipelineLayoutRelease(pipelineLayout);

	public static void Release(this WGPURenderPipeline renderPipeline) => wgpuRenderPipelineRelease(renderPipeline);

	public static void Release(this WGPUTextureView textureView) => wgpuTextureViewRelease(textureView);

	public static void Release(this WGPUBindGroup bindGroup) => wgpuBindGroupRelease(bindGroup);

	public static void Dispose(this WGPUTexture texture)
	{
		wgpuTextureDestroy(texture);
		wgpuTextureRelease(texture);
	}	

	public static void Dispose(this WGPUBuffer buffer)
	{
		wgpuBufferDestroy(buffer);
		wgpuBufferRelease(buffer);
	}
}
