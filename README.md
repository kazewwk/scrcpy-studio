# scrcpy Studio

scrcpy Studio 是 Genymobile [scrcpy 5.0.1](https://github.com/Genymobile/scrcpy/releases/tag/v5.0.1) 的中文 Windows 图形界面。它包含从源码编译的 Windows 客户端、Android 服务端，以及 ADB 和桌面运行时。

## 下载与启动

[下载 Windows x64 EXE](https://github.com/kazewwk/scrcpy-studio/releases/latest/download/ScrcpyStudio-1.0.0-win-x64.exe) · [Release 与 SHA-256](https://github.com/kazewwk/scrcpy-studio/releases/latest)

发行包面向 Windows x64，系统兼容范围参见 [Microsoft .NET 10 的 Windows 兼容列表](https://learn.microsoft.com/dotnet/core/install/windows#supported-versions)。运行 EXE 即可，首次启动会把内置组件解包到 `%LOCALAPPDATA%\ScrcpyStudio\runtime`。常规 USB 投屏需要开启 Android 的 USB 调试，并在设备上确认授权。

1. 接好 USB，在「连接」页刷新设备并选择对应序列号。无线调试可以在同一页填入手机显示的配对地址和配对码，再填入无线连接地址并连接。
2. 点击「开始投屏」。在 scrcpy 窗口操作手机，在「会话控制」页使用主页、返回、通知栏、旋转、剪贴板、画面和相机按钮。
3. 点击「停止」或关闭投屏窗口结束会话。录制期间使用「停止」可以让核心完成文件写入，运行日志会显示退出状态。

## 功能入口

| 功能 | 操作 |
| --- | --- |
| USB、TCP/IP、ADB 隧道 | 在「连接与隧道」配置设备选择、USB 转无线、本机监听端口与正向转发。 |
| 视频和音频 | 在对应页面设置分辨率、帧率、码率、编解码器、缓冲、采集来源和播放方式。 |
| 相机投屏 | 把「视频来源」设为 `camera`，再选镜头、尺寸、帧率、闪光灯和变焦。相机投屏需要 Android 12 或以上。 |
| 主屏、虚拟显示与应用启动 | 在「显示与窗口」启用指定显示器或虚拟显示，在「设备与电源」填写应用包名。 |
| 录制与音频文件 | 在「录制与投送」启用「保存录制文件」并选择路径。音频或视频可分别关闭。 |
| 键鼠、手柄与 OTG | 在「键鼠与手柄」选择 SDK、UHID 或 AOA 等模式。OTG 可使用 USB 键鼠控制，省去 USB 调试。 |
| 文件与 APK | 将文件拖入投屏窗口。普通文件按「推送目录」上传，APK 会交由设备安装。 |
| 查询与日志 | 在「连接」直接查询显示器、应用、编码器、相机和相机尺寸；在「查询与诊断」运行帮助和版本查询。 |

按 `Ctrl+F` 打开参数搜索，或在任一设置页面的搜索框输入中文名称、`--参数` 或短参数即可搜索整个目录。选中条目后，右侧可以启用、填写值并查看上游说明。带可选值的参数可用裸开关，或勾选「提供参数值」。下方可以复制当前配置对应的 PowerShell 命令，配置会在下次启动时恢复。

目录依据上游 `cli.c` 生成，包含 112 个 CLI 条目、17 个等价短参数别名、4 个环境变量。投屏窗口支持 [上游快捷键和鼠标手势](https://github.com/Genymobile/scrcpy/blob/v5.0.1/doc/shortcuts.md)。V4L2 参数可在目录查阅，运行需使用 Linux 客户端。具体音频、相机、OTG 与虚拟显示能力取决于 Android 版本、设备和驱动。

## 从源码构建

使用 Ubuntu 24.04 或相应 WSL 环境、.NET SDK 10.0.404、JDK 17、Android SDK Platform 36 和 Build Tools 36.0.0。将 `ANDROID_HOME` 指向 Android SDK，安装交叉编译工具后执行：

```sh
sudo apt-get install mingw-w64 mingw-w64-tools meson ninja-build nasm libz-mingw-w64-dev cmake autoconf automake libtool pkg-config
sdkmanager 'platforms;android-36' 'build-tools;36.0.0'
scripts/build.sh
```

产物在 `dist/`。脚本固定 scrcpy 源码提交，应用 [会话控制补丁](upstream/gui-control.patch)，重新构建 Windows 客户端和 Android 服务端。参数目录生成时会检查上游定义的每个条目。GitHub Actions 构建后会在 Windows 环境启动 EXE、检查内置组件并执行 CLI 冒烟测试。

## 许可证

GUI 与补丁采用 Apache-2.0。scrcpy 及其他随包组件的来源和许可证见 [第三方组件说明](THIRD_PARTY.md)。
