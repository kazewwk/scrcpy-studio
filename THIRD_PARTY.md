# 第三方组件

scrcpy Studio 是基于 Genymobile scrcpy 的独立项目。GUI 源码与 Windows 会话控制补丁采用 Apache-2.0。

| 组件 | 版本 | 许可证与源码 |
| --- | --- | --- |
| scrcpy 客户端和 Android 服务端 | 5.0.1 | [Apache-2.0，上游源码](https://github.com/Genymobile/scrcpy/tree/a60891aea193d92e7e5c3942700eca63f9d19a5f) |
| SDL | 3.4.18 | [zlib](https://github.com/libsdl-org/SDL/tree/release-3.4.18) |
| FFmpeg | 9.0.2 | [LGPL-2.1-or-later](https://ffmpeg.org/releases/ffmpeg-9.0.2.tar.xz) |
| dav1d | 1.5.4 | [BSD-2-Clause](https://downloads.videolan.org/pub/videolan/dav1d/1.5.4/) |
| libusb | 1.0.30 | [LGPL-2.1-or-later](https://github.com/libusb/libusb/tree/v1.0.30) |
| zlib | Ubuntu 24.04 MinGW 包 | [zlib](https://packages.ubuntu.com/noble/libz-mingw-w64-dev) |
| ADB 与 Windows ADB DLL | Platform Tools 37.0.1 | [AOSP ADB 源码](https://android.googlesource.com/platform/packages/modules/adb/) |
| .NET 与 WPF | 10.0.15 | [MIT，.NET](https://github.com/dotnet/runtime/tree/v10.0.15) · [MIT，WPF](https://github.com/dotnet/wpf/tree/v10.0.15) |
| Lucide 图标 | 1.77.0 | [ISC，部分图标源自 Feather（MIT）](https://github.com/lucide-icons/lucide/tree/1.77.0) |

EXE 启动后，组件目录中的 `LICENSES` 文件夹提供随包许可证及 ADB、.NET 的第三方声明。Release 中的 `third-party-source.tar.gz` 包含用于构建的 SDL、FFmpeg、dav1d、libusb 源码归档。FFmpeg 和 libusb 以 DLL 形式加载。上游编译选项及依赖校验值见 [scrcpy 依赖构建脚本](https://github.com/Genymobile/scrcpy/tree/a60891aea193d92e7e5c3942700eca63f9d19a5f/app/deps)。
