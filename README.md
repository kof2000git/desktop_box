# DesktopBox 桌面整理盒子

常驻 Windows 桌面的半透明「整理盒子」工具，把程序/文件/文件夹/网址拖进一个个盒子里分类。类似 Fences / 360 桌面助手的桌面整理功能。

技术栈：.NET 8 + WPF + WPF-UI + CommunityToolkit.Mvvm。

## 目录结构

```
desktop_assitant/
├── src/DesktopBox/          # 主程序源码
│   ├── Models/              # 数据模型
│   ├── Services/            # 持久化/桌面层/图标/拖放/主题/自启
│   ├── ViewModels/          # MVVM
│   ├── Views/ & Controls/   # 窗口与控件
│   └── Native/              # P/Invoke
├── src/DesktopBox.Tests/    # xUnit 单测(13 项)
├── publish/DesktopBox.exe        # 绿色单文件版
├── release/DesktopBoxSetup.exe   # 安装包
├── DesktopBox.iss           # Inno Setup 脚本
├── 使用说明.md               # 小白使用文档
└── docs/                    # 设计与计划
```

## 快速开始

- 用：见 [使用说明.md](使用说明.md)
- 改：`dotnet build DesktopBox.sln` → `dotnet test` → `dotnet publish`
- 打安装包：`ISCC.exe DesktopBox.iss`

## MVP 功能

常驻桌面浮层 · 多盒子拖放整理 · 双击打开 · 移动/缩放/重命名/删除 · 托盘菜单 · 开机自启 · 深浅色 · JSON 持久化 · 单实例。

## 设计要点

- 单主覆盖窗口贴 WorkerW 桌面层，失败降级为置顶窗口，保证稳定
- 不操作真实桌面图标，零 Shell 风险
- Service 层接口化、单测覆盖；Native/UI 层手动验证
