using Silk.NET.WebGPU;
using Silk.NET.Windowing;

namespace SilkWgpu;

public sealed unsafe class WebGpuHandler(IWindow window) : IDisposable
{
    private WebGPU wgpu = null!;
    private Instance* instance;
    private Surface* surface;
    private Device* device;
    private RenderPipeline* renderPipeline;

    public void Initialize()
    {
        // WebGPUを操作するためのAPIを取得
        // 基本的にはwebgpu.hで提供されている関数へのアクセスを提供
        wgpu = WebGPU.GetApi();

        instance = WebGpuInitializeUtil.CreateInstance(window, wgpu);
        surface = WebGpuInitializeUtil.CreateSurface(window, wgpu, instance);
        {
            var adapter = WebGpuInitializeUtil.CreateAdapter(wgpu, instance, surface);
            device = WebGpuInitializeUtil.CreateDevice(wgpu, adapter);
            // デバイス作成後はもうアダプタは不要なので解放
            wgpu.AdapterRelease(adapter);
        }

        const TextureFormat textureFormat = TextureFormat.Bgra8Unorm;
        WebGpuInitializeUtil.ConfigureSurface(window, wgpu, surface, device, textureFormat);
        WebGpuInitializeUtil.SetErrorCallback(wgpu, device);
        {
            var shaderModule = WebGpuInitializeUtil.CreateShaderModule(wgpu, device, "Shaders/triangle.wgsl");
            renderPipeline = WebGpuInitializeUtil.CreateRenderPipeline(wgpu, device, shaderModule, textureFormat);
            // RenderPipeline作成後はシェーダーモジュールは不要なので解放
            wgpu.ShaderModuleRelease(shaderModule);
        }

        // ウィンドウのRenderイベントを購読
        window.Render += Render;
    }

    public void Dispose()
    {
        // 購読解除
        window.Render -= Render;
        // リソースの解放
        wgpu.RenderPipelineRelease(renderPipeline);
        wgpu.SurfaceUnconfigure(surface);
        wgpu.DeviceDestroy(device);
        wgpu.SurfaceRelease(surface);
        wgpu.InstanceRelease(instance);
    }

    private void Render(double _)
    {
        SurfaceTexture surfaceTexture = default;
        // 描画する対象のTextureをSurfaceから取得
        wgpu.SurfaceGetCurrentTexture(surface, ref surfaceTexture);
        // TextureからTextureViewを作成
        var textureView = wgpu.TextureCreateView(surfaceTexture.Texture, null);

        var colorAttachments = stackalloc RenderPassColorAttachment[1];
        colorAttachments[0] = new RenderPassColorAttachment
        {
            View = textureView,
            LoadOp = LoadOp.Clear,
            // 青っぽい色
            ClearValue = new Color(0f / 255f, 121f / 255f, 255f / 255f, 1f),
            StoreOp = StoreOp.Store
        };

        RenderPassDescriptor renderPassDescriptor = new()
        {
            ColorAttachments = colorAttachments,
            ColorAttachmentCount = 1
        };

        var commandEncoder = wgpu.DeviceCreateCommandEncoder(device, null);
        // RenderPassを開始
        var renderPassEncoder = wgpu.CommandEncoderBeginRenderPass(commandEncoder, renderPassDescriptor);
        // RenderPassにRenderPipelineをセット
        // RenderPipelineは複数セット可能（異なる図形を描画できる）
        wgpu.RenderPassEncoderSetPipeline(renderPassEncoder, renderPipeline);
        // シェーダーに渡す情報を設定
        // 頂点数3でインデックスは0から
        // 頂点シェーダーがmain_vs(0)、main_vs(1)、main_vs(2)のように呼び出されるようなイメージ
        // 今回の頂点シェーダーの実装ではindex（渡されてきた引数）で条件分岐を行い、対応する画面上の座標を返している（triangle.wgsl参照）
        wgpu.RenderPassEncoderDraw(renderPassEncoder, vertexCount: 3, 1, firstVertex: 0, 0);
        // RenderPassを終了
        wgpu.RenderPassEncoderEnd(renderPassEncoder);
        // ブロック内は順不同
        {
            // RenderPassEncoderEnd後から解放可能：TextureView, RenderPassEncoder
            wgpu.TextureViewRelease(textureView);
            wgpu.RenderPassEncoderRelease(renderPassEncoder);
        }
        // CommandBufferをCommandEncoderから構築
        var commandBuffer = wgpu.CommandEncoderFinish(commandEncoder, null);
        // CommandEncoderFinish後から解放可能：CommandEncoder
        wgpu.CommandEncoderRelease(commandEncoder);
        var queue = wgpu.DeviceGetQueue(device);
        wgpu.QueueSubmit(queue, 1, ref commandBuffer);
        // ブロック内は順不同
        {
            // QueueSubmit後から解放可能：Surface, Queue
            // QueueSubmit後に実行：SurfacePresent
            wgpu.CommandBufferRelease(commandBuffer);
            wgpu.QueueRelease(queue);
            wgpu.SurfacePresent(surface);
        }
        // SurfacePresent後から解放可能：Texture
        wgpu.TextureRelease(surfaceTexture.Texture);
    }
}