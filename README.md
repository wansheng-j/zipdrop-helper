# ZipDrop Helper（拖拽解压助手）

Windows 小工具：解决 macOS / 手机压缩包在 Windows 下**中文文件名乱码**的问题，并自动清理 `__MACOSX` 垃圾文件。

**纯原生**：单个 C# 源文件，无第三方依赖，Windows 10/11 自带编译器即可构建，生成的 exe 只有 17KB。

## 为什么会有乱码？

macOS（Finder「压缩」）生成的 zip 会把文件名按 **UTF-8** 写入，但**不设置 zip 头里的 UTF-8 标志位**（bit 11）。Windows 资源管理器遇到没有标志位的条目时，按本机 ANSI 代码页（中文系统 = GBK）解码，于是中文名全部变乱码，例如 `苹果香蕉梨` 变成 `鑻规灉棣欒晧姊�`（末尾的残缺字符是乱码的常见特征）。同时 macOS 压缩包还会附带 `__MACOSX` 目录和 `._` 开头的元数据文件（无用垃圾）。

## 功能

- 拖拽解压：把 zip 拖进窗口即可，支持一次多个，窗口自动置顶
- 自动识别文件名编码（见下方「原理」）
- 自动清理 `__MACOSX` 目录与 `._*` 文件
- 防路径穿越；输出目录自动避重（`xxx_解压`、`xxx_解压_2`…）
- 解压后自动打开输出文件夹（可勾选关闭）

## 使用

1. 双击 `ZipDrop.exe` 打开窗口
2. 把 zip（可多选）拖进窗口
3. 完成

也支持把文件路径作为命令行参数传入（窗口打开后立即处理），可自行配置到右键菜单或「发送到」。

自测模式：`ZipDrop.exe --selftest "文件.zip"`（无界面，直接输出结果，用于脚本测试）。

## 新手安装（零基础 3 步）

不用编译、不用装任何环境，Windows 10/11 双击即用：

1. **下载 `ZipDrop.exe`**（绿色免安装，仅 18KB）——两种方式任选其一：
   - **方式一**：这个仓库根目录里**直接就有** `ZipDrop.exe`（点「Clone 下载 ZIP」或克隆仓库都能拿到）
   - **方式二**：Releases 页面下载
     - GitHub：<https://github.com/wansheng-j/zipdrop-helper/releases/latest>
     - Gitee：<https://gitee.com/wansheng051112/zipdrop-helper/releases>
   - 友情提示：如果“仓库里没看到 exe”，确认下载的是**最新版本**（首次提交后的版本才带 exe）
2. **双击运行**：第一次可能弹出「Windows 已保护你的电脑」→ 点 **「更多信息」→「仍要运行」**（程序没有签名，这是正常提示，之后不会再问）
3. **拖进去**：把 zip 文件拖进窗口 → 自动解压完成，自动弹出结果文件夹

想把它放到桌面长期用：右键 `ZipDrop.exe` → 发送到 → 桌面快捷方式（或直接拖到桌面上）。

> 注意：请使用 Releases 里的 `ZipDrop.exe`，不要运行其他来源的 exe。

## 构建

**方式一（推荐，零依赖）**：双击 `build.cmd`，使用 Windows 自带的 .NET Framework 编译器（`csc.exe`），无需安装任何东西。

**方式二**：用 Visual Studio 打开 `ZipDrop.csproj` 生成（目标框架 net48）。

## 原理

Windows / .NET 自带的 zip 读取器会按系统代码页自动解码文件名，无法拿到原始字节，这是乱码的根源。本工具绕过它：

1. 直接解析 zip 文件尾部的**中央目录（Central Directory）**，读取每个条目的**原始文件名字节**和通用标志位（bit 11 = UTF-8 标志）
2. 解码规则：
   - 有 UTF-8 标志位 → 按 UTF-8 解码
   - 无标志位：字节是合法 UTF-8 → 按 UTF-8（macOS / 手机包）；否则 → 按 GBK（中文 Windows 压缩的包）
3. 用 `System.IO.Compression.ZipArchive` 解压数据，跳过 `__MACOSX` / `._*` 条目
4. 文件名解码用「严格模式」（`DecoderFallback.ExceptionFallback`），非法字节抛异常而不是静默替换

界面为 WPF 拖拽窗口（纯代码构建，未用 XAML），目标 .NET Framework 4.8（Win10/11 自带）。

## 平台

仅 Windows（依赖 WPF / .NET Framework 4.8）。Win10 / Win11 可直接运行。

## 许可

**PolyForm Noncommercial License 1.0.0**（非商用开源协议）

- ✅ 任何人可以免费使用、学习、修改、分发（个人 / 学习 / 教育 / 公益等**非商业**用途）
- ❌ **禁止商用**：不得用于任何商业目的（商业产品、盈利项目、企业内部商业用途等）
- 📌 使用或分发时须保留协议全文与版权声明（`Required Notice: Copyright 2026 wansheng`）

代码含中文注释，欢迎 fork / 改进（非商业用途）。
