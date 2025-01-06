using Silk.NET.Windowing;
using SilkWgpu;

// 既定値と変更したい値を指定してWindowOptionsを作成
var windowOptions = WindowOptions.Default with
{
    // タイトル
    Title = "Silk.NET WebGPU",
    // グラフィクスAPIを指定しない
    API = GraphicsAPI.None
};

// オプションを指定してウィンドウを作成
var window = Window.Create(windowOptions);
// WebGPU初期化前にウィンドウを初期化する必要がある
window.Initialize();
using WebGpuHandler webGpuHandler = new(window);
webGpuHandler.Initialize();
// ウィンドウを開く
window.Run();