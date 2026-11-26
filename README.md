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

1. 双击 **Intranet.Tools.ImageCompress-Setup-x64.exe**。安装器为当前用户安装标准 WPF 桌面程序，并注册 Windows 11 第一层右键菜单。首次注册时可能出现信任菜单发布者证书的管理员提示。[下载 EXE 安装器](https://github.com/lzl32947/Intranet.Tools.ImageCompress/releases/latest/download/Intranet.Tools.ImageCompress-Setup-x64.exe)
2. 主程序使用系统的 **.NET 10 桌面运行时 x64**。缺少时请在微软下载页选择「.NET Desktop Runtime」的 Windows x64 安装程序，完成后即可启动应用。[微软运行时下载页](https://dotnet.microsoft.com/zh-cn/download/dotnet/10.0)
3. 选中一张或多张图片，右键点击 **压缩图片**。固定档位直接开始，「压缩…」可设置自定义大小。每张图片独立核验上限，1 KB 等于 1000 字节。

结果写入原图所在文件夹，例如 `照片_500KB.jpg`。遇到重名会自动编号。同格式且已经达标的文件会跳过。JPG 和 PNG 保持原格式，其他支持的静态图片可转 JPG 或 PNG，包括 WebP、BMP、GIF、TIFF、HEIC/HEIF 和 AVIF。遇到动画或多页图片会提示跳过。

## 构建和打包

构建机需要 Windows 11 x64、.NET 10 SDK、Visual Studio 的「使用 C++ 的桌面开发」工具链、Windows SDK 的 MakeAppx 和 SignTool，以及 Inno Setup 7.1 或更高的 7.x 版本。[Inno Setup](https://jrsoftware.org/isinfo.php)

使用 .NET 10 SDK 打开或构建 [Intranet.Tools.ImageCompress.slnx](Intranet.Tools.ImageCompress.slnx)。

在源码根目录使用 PowerShell 7.4 或更高版本构建。

```powershell
./packaging/Build.ps1
```

EXE 安装器和 `SHA256SUMS.txt` 位于 `artifacts/exe/<四段版本号>/`。可通过 `-Compiler` 指定 ISCC.exe 的路径，通过 `-CertificateThumbprint` 选择当前用户已有的签名证书。默认版本取自 `Directory.Build.props`。发布更新时应沿用原签名证书，并提高版本号。

WPF 主程序以框架依赖模式发布到普通文件夹。安装器内的微型 MSIX 仅声明菜单身份，图片编解码库和 EXE 都安装在该包外。菜单通过系统的外部位置注册接口指向实际安装目录。[微软菜单身份文档](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/grant-identity-to-nonpackaged-apps)

## 项目

- `src/Desktop` 包含 WPF 主窗口、选区配置、任务进度和结果。
- `src/Engine` 使用 Magick.NET Q16 处理静态图片，按实际字节数筛选编码结果，并在同目录原子发布副本。
- `src/Shell` 生成 x64 原生 COM DLL，注册为 Windows 11 的 IExplorerCommand。资源管理器只传递选区。
- `src/Shared` 是主程序和扩展共用的参数存储与选区传递代码。当前用户的配置位于 `%LOCALAPPDATA%\Intranet.Tools.ImageCompress`。

从 v1.0.0 升级时，直接运行 EXE 安装器即可保留上次参数、更新菜单。安装器可从 Windows 的「设置 → 应用 → 安装的应用」卸载。[安装与卸载说明](packaging/安装说明.txt)

项目使用 MIT 许可证，版权所有者为 lzl32947。第三方组件随附各自的许可与声明。[MIT 许可证](LICENSE)

## 验证

使用 Pillow、numpy、pillow-heif 运行 `python tests/make_fixtures.py <空测试目录>` 生成本机测试图，再运行 `dotnet run --project tests/Checks -c Release -r win-x64 -- <测试目录>`。测试覆盖字节上限、无损优化、旋转、16 位与透明 PNG、静态格式转换、动画识别、重名及参数记忆。

安装后，`dotnet run --project tests/ExplorerProbe -c Release -r win-x64 -- 0 <图片路径...>` 可通过系统 COM 注册读取菜单标题、顺序与可用状态。数字 1 至 5 可触发对应的真实菜单命令。

可以用 `Get-FileHash -Algorithm SHA256` 核对 EXE 与 Release 附带的 `SHA256SUMS.txt`。构建脚本还会检查主程序的共享框架声明，并验证菜单身份包的签名和数据块。
