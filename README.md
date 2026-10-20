# 鱼灯制作工坊

基于 Unity 的鱼灯制作体验，包含扎骨架、裁剪糊纸、描线上色和装灯展示，以及可步行到达的工坊庭院与村巷。

## 打开工程

- Unity 版本：6000.6.5f1。
- 正式场景：`Assets/Yudeng/Scenes/FishLanternWorkshop.unity`。
- Windows 发布目标：Windows 64 位，IL2CPP。
- 工程使用 OpenXR、XR Interaction Toolkit、Input System 和 URP，具体依赖以 `Packages/manifest.json` 为准。
- 模型、贴图及其他二进制资源使用 Git LFS；克隆时需要安装 Git LFS，并执行 `git lfs pull` 获取资源。

## 输入方式

默认尝试启动 VR 设备模式。连接头显并准备好设备的 OpenXR 运行环境后启动程序即可；设备启动失败时自动回到键盘模拟。

启动参数 `--desktop` 可直接使用键盘模拟：

```text
YudengWorkshop.exe --desktop
```

键盘与 VR 共用同一套抓取、工具操作、空间按钮和制作流程。

- `F`：切换行走／制作。
- `1/2/3`：选择头部／左手／右手。
- `G`：握持开关；`T`：扳机开关。
- 空间按钮需要按一次 `T` 压下，再按一次 `T` 释放。

完整说明见 [键盘操作指南](Docs/PlayerGuides/键盘操作指南.md)。

## 资源与范围

仓库包含正式场景、运行代码、场景引用资源、项目配置和用户操作说明。美术制作源工程、临时接入脚本、预览、开发记录、测试产物和编译包保留在本地，不作为本次交付内容。

外部资源及许可见 [素材来源说明](Docs/PlayerGuides/素材来源说明.md)。Windows 构建成功不代表头显兼容性、完整流程或性能已经完成验收。
