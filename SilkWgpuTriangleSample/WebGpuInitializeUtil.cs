using System.Runtime.InteropServices;
using Silk.NET.Core.Contexts;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;
using Silk.NET.Windowing;

namespace SilkWgpu;

public static class WebGpuInitializeUtil
{
    public static unsafe Instance* CreateInstance(IWindow window, WebGPU wgpu)
    {
        // ウィンドウを供給しているプラットフォームを取得
        // 例1: macOS    : Glfw, Cocoa
        // 例2: Windows  : Glfw, Win32
        var kind = window.Native!.Kind;
        InstanceBackend backend;
        if (kind.HasFlag(NativeWindowFlags.Win32))
        {
            // Windowsの場合はDX12を使用
            backend = InstanceBackend.DX12;
        }
        else if (kind.HasFlag(NativeWindowFlags.Cocoa))
        {
            // macOSの場合はMetalを使用
            backend = InstanceBackend.Metal;
        }
        else
        {
            // その他の場合はVulkanを使用
            backend = InstanceBackend.Vulkan;
        }

        // Silk.NET.WebGPU.Extensions.WGPUが提供するInstanceExtrasを使用してバックエンドを指定できる
        InstanceExtras extras = new()
        {
            Backends = backend
        };

        // InstanceDescriptor.NextInChainにInstanceExtras.Chainを設定
        InstanceDescriptor descriptor = new()
        {
            NextInChain = &extras.Chain
        };

        // Instance(JavaScript APIにおけるnavigator.gpuに相当するもの)を作成
        // https://developer.mozilla.org/ja/docs/Web/API/Navigator/gpu
        return wgpu.CreateInstance(descriptor);
    }

    public static unsafe Surface* CreateSurface(IWindow window, WebGPU wgpu, Instance* instance)
    {
        // 与えられたIWindowに紐づけられたSurfaceを作成
        return window.CreateWebGPUSurface(wgpu, instance);
    }

    public static unsafe Adapter* CreateAdapter(WebGPU wgpu, Instance* instance, Surface* surface)
    {
        Adapter* adapter = null;
        RequestAdapterOptions options = new()
        {
            CompatibleSurface = surface,
            // RequestAdapterOptions.backendTypeは既にサポートされていない
            // 代わりにInstance作成時のInstanceExtras.Backendを使用する
            // BackendType = BackendType.Null,
            PowerPreference = PowerPreference.HighPerformance
        };

        var callback = PfnRequestAdapterCallback.From(
            (status, wgpuAdapter, msgPtr, _) =>
            {
                if (status == RequestAdapterStatus.Success)
                {
                    // adapterに値を設定
                    adapter = wgpuAdapter;
                }
                else
                {
                    var msg = Marshal.PtrToStringAnsi((IntPtr)msgPtr) ?? string.Empty;
                    throw new InvalidOperationException($"Adapter作成に失敗: {msg}");
                }
            });

        // Adapterを作成
        // 参考：https://eliemichel.github.io/LearnWebGPU/getting-started/adapter-and-device/the-adapter.html
        wgpu.InstanceRequestAdapter(instance, options, callback, null);
        // PfnRequestAdapterCallbackは実際には同期的に呼ばれるため、成功していれば値が設定されている
        return adapter;
    }

    public static unsafe Device* CreateDevice(WebGPU wgpu, Adapter* adapter)
    {
        Device* device = null;
        var callback = PfnRequestDeviceCallback.From(
            (status, wgpuDevice, msgPtr, _) =>
            {
                if (status == RequestDeviceStatus.Success)
                {
                    // deviceに値を設定
                    device = wgpuDevice;
                }
                else
                {
                    var msg = Marshal.PtrToStringAnsi((IntPtr)msgPtr) ?? string.Empty;
                    throw new InvalidOperationException($"Device作成に失敗: {msg}");
                }
            });

        DeviceDescriptor descriptor = default;
        // Deviceを作成
        // 参考：https://eliemichel.github.io/LearnWebGPU/getting-started/adapter-and-device/the-device.html
        wgpu.AdapterRequestDevice(adapter, descriptor, callback, null);
        // PfnRequestDeviceCallbackは実際には同期的に呼ばれるため、成功していれば値が設定されている
        return device;
    }

    public static unsafe void ConfigureSurface(
        IWindow window,
        WebGPU wgpu,
        Surface* surface,
        Device* device,
        TextureFormat textureFormat)
    {
        // 内部的なswap chainの構成を行う
        SurfaceConfiguration configuration = new()
        {
            Device = device,
            // 幅と高さをウィンドウのサイズに合わせる
            // ウィンドウサイズが変更された場合は再設定する
            Width = (uint)window.Size.X,
            Height = (uint)window.Size.Y,
            Format = textureFormat,
            // 先入れ先出し
            PresentMode = PresentMode.Fifo,
            Usage = TextureUsage.RenderAttachment
        };

        wgpu.SurfaceConfigure(surface, configuration);
    }

    public static unsafe void SetErrorCallback(WebGPU wgpu, Device* device)
    {
        var callback = PfnErrorCallback.From((type, msgPtr, _) =>
        {
            var msg = Marshal.PtrToStringAnsi((IntPtr)msgPtr) ?? string.Empty;
            Console.WriteLine($"Unhandled error: {type}: {msg}");
        });

        // エラーコールバックを設定
        wgpu.DeviceSetUncapturedErrorCallback(device, callback, null);
    }

    public static unsafe ShaderModule* CreateShaderModule(WebGPU wgpu, Device* device, string shaderPath)
    {
        // 指定されたパスのファイルを読み込む
        var shaderCode = File.ReadAllText(shaderPath);
        // WGSL形式のシェーダーモジュールを作成
        ShaderModuleWGSLDescriptor wgslDescriptor = new()
        {
            Code = (byte*)Marshal.StringToHGlobalAnsi(shaderCode)
        };
        wgslDescriptor.Chain.SType = SType.ShaderModuleWgslDescriptor;

        ShaderModuleDescriptor descriptor = new()
        {
            NextInChain = &wgslDescriptor.Chain
        };

        return wgpu.DeviceCreateShaderModule(device, descriptor);
    }

    public static unsafe RenderPipeline* CreateRenderPipeline(
        WebGPU wgpu,
        Device* device,
        ShaderModule* shaderModule,
        TextureFormat textureFormat,
        string vertexEntryPoint = "main_vs",
        string fragmentEntryPoint = "main_fs")
    {
        VertexState vertexState = new()
        {
            Module = shaderModule,
            EntryPoint = (byte*)Marshal.StringToHGlobalAnsi(vertexEntryPoint)
        };

        BlendComponent blendComponent = new()
        {
            SrcFactor = BlendFactor.One,
            DstFactor = BlendFactor.OneMinusSrcAlpha,
            Operation = BlendOperation.Add
        };
        var blendStates = stackalloc BlendState[1];
        blendStates[0] = new BlendState
        {
            Color = blendComponent,
            Alpha = blendComponent
        };

        var colorTargetState = stackalloc ColorTargetState[1];
        colorTargetState[0] = new ColorTargetState
        {
            WriteMask = ColorWriteMask.All,
            Format = textureFormat,
            Blend = blendStates
        };

        var fragmentState = new FragmentState
        {
            Module = shaderModule,
            EntryPoint = (byte*)Marshal.StringToHGlobalAnsi(fragmentEntryPoint),
            Targets = colorTargetState,
            TargetCount = 1
        };

        RenderPipelineDescriptor descriptor = new()
        {
            Vertex = vertexState,
            Fragment = &fragmentState,
            // MSAAに関する設定
            Multisample = new MultisampleState
            {
                // 全てのサンプルを有効化
                // 0xFFFFFFFFuやuint.MaxValueなどでも同値
                Mask = ~0u,
                // ピクセルごとの計算サンプル数
                Count = 1,
                AlphaToCoverageEnabled = false
            },
            Primitive = new PrimitiveState
            {
                // 裏面をカリング（描画しない）
                CullMode = CullMode.Back,
                // CCW: Counter Clock Wise（反時計回り）
                // 前面の定義を
                // 「頂点が反時計回りに並べられている方」とする
                FrontFace = FrontFace.Ccw,
                // 3つの頂点ごとに三角形を描画
                Topology = PrimitiveTopology.TriangleList,
            }
        };

        return wgpu.DeviceCreateRenderPipeline(device, descriptor);
    }
}