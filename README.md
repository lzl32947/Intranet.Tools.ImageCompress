<p align="center"><img src="packaging/Assets/Square150.png" width="72" height="72" alt="压缩图片图标" /></p>

# Intranet.Tools.ImageCompress

[![Windows 11 x64](https://img.shields.io/badge/Windows_11-x64-0078D4)](https://github.com/lzl32947/Intranet.Tools.ImageCompress/releases/latest)
[![最新版本](https://img.shields.io/github/v/release/lzl32947/Intranet.Tools.ImageCompress)](https://github.com/lzl32947/Intranet.Tools.ImageCompress/releases/latest)
![WPF · .NET 10](https://img.shields.io/badge/WPF-.NET_10-512BD4)
[![MIT License](https://img.shields.io/badge/License-MIT-176956)](LICENSE)
![Google Antigravity 参与开发](https://img.shields.io/badge/AI_%E5%8D%8F%E4%BD%9C-Antigravity-7149B6)
![OpenAI Codex 参与开发](https://img.shields.io/badge/AI_%E5%8D%8F%E4%BD%9C-Codex-176956)

面向 Windows 11 x64 的 WPF 图片压缩工具。右键提供配置、上次参数、500 KB、1000 KB 和主窗口五项命令。

## 安装与使用

1. 下载并解压 **Intranet.Tools.ImageCompress-win-x64.zip**，其中包含签名的 MSIX、公开证书和中文安装说明。[下载最新安装包](https://github.com/lzl32947/Intranet.Tools.ImageCompress/releases/latest/download/Intranet.Tools.ImageCompress-win-x64.zip)
2. 首次安装时，按说明将公开证书导入本地计算机的「受信任人」，然后双击 MSIX 安装。安装包内置 .NET 10 桌面运行时。[安装说明](packaging/安装说明.txt)
3. 选中一张或多张图片，右键点击 **压缩图片**。固定档位直接开始，「压缩…」可设置自定义大小。每张图片独立核验上限，1 KB 等于 1000 字节。

结果写入原图所在文件夹，例如 `照片_500KB.jpg`。遇到重名会自动编号。同格式且已经达标的文件会跳过。JPG 和 PNG 保持原格式，其他支持的静态图片可转 JPG 或 PNG，包括 WebP、BMP、GIF、TIFF、HEIC/HEIF 和 AVIF。遇到动画或多页图片会提示跳过。

## 构建和打包

构建机需要 Windows 11 x64、.NET 10 SDK、Visual Studio 的「使用 C++ 的桌面开发」工具链（供 C# Native AOT 链接）以及 Windows 10/11 SDK 的 MakeAppx 和 SignTool。

使用 .NET 10 SDK 打开或构建 [Intranet.Tools.ImageCompress.slnx](Intranet.Tools.ImageCompress.slnx)。

在源码根目录用 PowerShell 7 运行：

```powershell
./packaging/Build.ps1
```

产物位于 `artifacts/distribution`。构建脚本在当前用户的个人证书存储中生成或复用专用、不可导出的代码签名私钥。生成的分享 ZIP 包含 MSIX、公开的 CER 和安装说明。更新可运行 `./packaging/Build.ps1 -Version 1.0.1.0`，请保留原签名证书。

## 项目

- `src/Desktop` 包含 WPF 主窗口、选区配置、任务进度和结果。
- `src/Engine` 使用 Magick.NET Q16 处理静态图片，按实际字节数筛选编码结果，并在同目录原子发布副本。
- `src/Shell` 生成 x64 原生 COM DLL，注册为 Windows 11 的 IExplorerCommand。资源管理器只传递选区。
- `src/Shared` 是主程序和扩展共用的参数存储与选区传递代码。设置存入当前包的 LocalState。

修改发布者时，需同步更新清单中的 Publisher 以及共享状态使用的包系列名称。第三方图像依赖的版权与许可随安装包提供。

项目使用 MIT 许可证，版权所有者为 lzl32947。第三方组件随附各自的许可与声明。[MIT 许可证](LICENSE)

## 验证

使用 Pillow、numpy、pillow-heif 运行 `python tests/make_fixtures.py <空测试目录>` 生成本机测试图，再运行 `dotnet run --project tests/Checks -c Release -r win-x64 -- <测试目录>`。测试覆盖字节上限、无损优化、旋转、16 位与透明 PNG、静态格式转换、动画识别、重名及参数记忆。

安装后，`dotnet run --project tests/ExplorerProbe -c Release -r win-x64 -- 0 <图片路径...>` 可通过系统 COM 注册读取菜单标题、顺序与可用状态。数字 1 至 5 可触发对应的真实菜单命令。

使用 PowerShell 7.4 或以上运行 `./tests/Verify-Package.ps1 -Package <MSIX路径> -Certificate <CER路径>` 可离线校验发布者、签名及安装包各文件的数据块。
