# Unity 6 与 Android 开发环境

日期：2026-10-29（Asia/Shanghai）。范围：独立 LingChat XR 的本机开发工具；不修改 LingChat 桌面项目、角色设置或资源。

## 结果

复用本机已安装的 **Unity 6000.4.0f1 / revision 8cf496087c8f**，并补装 Android Build Support、SDK、NDK 和 OpenJDK。没有新增另一份 Unity 6，没有移除旧 Unity 2022。

工具链已通过可执行程序检查，并实际编译、链接一份 Android ARM64 共享库。**本批未构建 APK、未连接或安装到 Quest。** 用户确认 Quest 3 尚未到货；这些工具链证据不能代替透视、手追、空间稳定性、帧率或真机验收。

## 实际路径和版本

编辑器：`D:\GameCreator\Unity\Editor\Unity.exe`。

下表路径都位于 `D:\GameCreator\Unity\Editor\Data\PlaybackEngines\AndroidPlayer` 下。

| 组件 | 相对路径 | 核验结果 |
| --- | --- | --- |
| Android Build Support | 本目录 | 官方 CLI 重新探测为已安装 |
| Android SDK | `SDK` | API 34、35、36 的 `android.jar` 存在 |
| Platform Tools | `SDK\platform-tools` | `adb version`：1.0.41 / 36.0.0-13206524，退出 0 |
| Build Tools | `SDK\build-tools\36.0.0` | `aapt2 version`：2.20-13193326，退出 0 |
| Command Line Tools | `SDK\cmdline-tools\16.0` | 官方安装成功；本批没有运行 sdkmanager |
| CMake | `SDK\cmake\3.22.1` | `cmake --version`：3.22.1-g37088a8-dirty，退出 0 |
| NDK | `NDK` | r27c / 27.2.12479018；clang 18.0.3，退出 0；ARM64 共享库编译链接通过 |
| OpenJDK | `OpenJDK` | Temurin 17.0.9+9，`java -version` 退出 0 |

Unity 的 External Tools 应使用这份编辑器附带的 JDK、SDK 和 NDK。NDK 正确路径是上表的 `NDK` 根目录，**不再使用** `NDK\android-ndk-r27c`。本批没有改写系统 JAVA_HOME、TEMP、TMP 或系统代理；只有安装命令进程的临时目录指向 D 盘工作区缓存。

## 安装和 NDK 修复记录

使用已有官方 CLI：`C:\Program Files\Unity Hub\resources\cli\unity.exe`，版本 1.0.0-beta.8。

安装命令（已经执行，不需重复下载）：

```powershell
& 'C:\Program Files\Unity Hub\resources\cli\unity.exe' install-modules -e 6000.4.0f1 -m android android-sdk-ndk-tools android-open-jdk-17.0.9+9 --cm --accept-eula --yes --non-interactive --proxy-disable --no-banner
```

官方依赖队列共 11 项，记录下载量约 3.02 GB、安装量约 10.37 GB。首轮安装返回退出码 6：10 项成功，`android-ndk-r27c` 报 `INSTALL_ERROR`。失败日志完整保留在 `evidence/unity6-setup-install-direct.log`，不能把首轮安装说成全部成功。

NDK 已解压到额外一层 `NDK\android-ndk-r27c`，未完成模块元数据要求的归位。核对缓存 `C:\Users\fangcheng612\AppData\Roaming\UnityHub\downloads\android-ndk-r27c-windows.zip` 与解压内容：**7,935 个文件的存在性和大小检查，0 项不一致**。此检查不是逐文件密码学哈希检查。

确认源和目标解析后的绝对路径均在该编辑器的 `AndroidPlayer\NDK` 内、目标无同名冲突后，用 PowerShell `Move-Item -LiteralPath` 将 19 个顶层项移到 NDK 根。没有删除缓存；原空包装目录保留。没有修改 NDK 内容或手写安装成功标志。

之后执行官方重新探测：

```powershell
& 'C:\Program Files\Unity Hub\resources\cli\unity.exe' editor module refresh 6000.4.0f1 --json --proxy-disable
& 'C:\Program Files\Unity Hub\resources\cli\unity.exe' editor module list 6000.4.0f1 --json --no-banner
```

`refresh` 返回 `success: true`，Android Build Support、Android SDK & NDK Tools、OpenJDK 均为 `isInstalled: true`。随后保存的官方列表是 `evidence/unity6-setup-module-status.json`；重新探测前的原始模块记录保存在 `evidence/unity6-setup-modules-before-refresh.json`。

## 可复现验证

在 PowerShell 执行：

```powershell
& 'D:\GameCreator\Document\DesktopSmallBlueFish\evidence\unity6-verify-environment.ps1'
```

脚本依次运行 Unity 版本查询、adb、java、aapt2、CMake、clang；检查 SDK 平台文件；用 `--target=aarch64-linux-android32 -shared -fPIC` 编译和链接 `evidence/unity6-ndk-probe.c`。随后 `llvm-readelf -h` 确认产物为 **ELF64 / AArch64 / DYN**。

成功日志：`evidence/unity6-setup-verify-2.log`。产物：`evidence/liblingchat_xr_ndk_probe.so`。第一次验证脚本因 PowerShell 未等待 GUI 编辑器进程、未取得退出码而中断，日志 `evidence/unity6-setup-verify.log` 保留；脚本已改为管道等待，第二次完整通过。这不是 Unity 或 NDK 编译失败。

没有启动 adb 设备服务、没有运行 `adb devices`，也没有头显连接证据。本批只完成工具链准备；项目 Android/IL2CPP 构建、APK 安装、Quest 关闭 PC 后独立互动均需后续独立验证。
