# Elysium —《虚拟现实技术》课程作业（AR 交互 + 碰撞检测）

基于 Unity 官方 **AR Mobile Template** 改写。删除了模板里的演示功能（物体生成菜单、引导提示、调试菜单、教程等），只保留 AR 核心（AR Session、XR Origin (AR Rig)、平面检测、方向光），并实现了作业要求的两个功能。

## 作业要求完成情况

1. **放置 1 个模型 + 交互操作**：放置一个立方体，可用手势交互 —— 单指拖动移动、双指捏合缩放、双指旋转（编辑器里可用鼠标拖动测试）。
2. **放置 2 个模型 + 碰撞检测**：放置立方体和球体两个模型，用交互把它们相互靠近，触发碰撞检测，两者变绿并提示“碰撞检测成功”。

## 运行环境

- Unity **6000.3.10f1**（Unity 6）
- AR Foundation 6.3.3 + ARKit 6.3.3（iOS）
- XR Interaction Toolkit 3.3.1
- URP 17.3.0

## 怎么用

1. 用 Unity Hub 打开本项目，确认打开的版本是 6000.3.x。
2. 打开场景 `Assets/Scenes/Assignment.unity`（已设为 Build Settings 里的唯一场景）。
3. 直接 Build 到 iPhone（或先在编辑器里用 XR Simulation 预览）。

**手机上的操作：**

- 先用摄像头扫一下地面/桌面，等检测到平面。
- 底部三个按钮切换模式：
  - **放置立方体** —— 轻点屏幕，把红色立方体放到平面上（没检测到平面时会放到相机前方 0.5 米）。
  - **放置球体** —— 轻点屏幕，把蓝色球体放到平面上。
  - **自由交互** —— 单指拖动移动模型；双指捏合缩放、双指旋转。
- 把立方体和球体拖到一起，两者**碰撞时会变绿**，顶部提示“碰撞检测成功”；分开后恢复原色。

## 文件结构

- `Assets/Scripts/AssignmentController.cs` —— 作业全部逻辑（放置、交互、碰撞检测），通过 `[RuntimeInitializeOnLoadMethod]` 运行时自动挂载，无需在场景里手动添加。
- `Assets/Scenes/Assignment.unity` —— 干净的主场景（AR Session + XR Origin (AR Rig) + 方向光 + EventSystem）。
- `Assets/MobileARTemplateAssets/` —— 只保留了 AR 平面可视化所需（遮挡平面 prefab、材质、着色器、`ARPlaneMeshVisualizerFader.cs`）。
- `Assets/Samples/XR Interaction Toolkit/` —— 保留 XR Origin (AR Rig) 等核心 prefab，删除了两个 demo 场景（ARDemoScene、DemoScene）。

## 相对原模板的改动

| 项目 | 处理 |
| --- | --- |
| 物体生成菜单（Object Menu / Create Button） | 删除 |
| 引导提示（TapToPlace / ScanSurfaces / Move / Scale / Rotate / InputHints / GreetingCTA） | 删除 |
| 调试菜单（DebugMenu / Options Modal） | 删除 |
| 教程（Tutorial 目录） | 删除 |
| Object Spawner（示例物体生成器） | 从场景中移除 |
| 示例场景（ARDemoScene、DemoScene） | 删除 |
| 演示脚本（ARTemplateMenuManager、GoalManager、CutoutMaskUI 等） | 删除 |
| AR 核心（AR Session、XR Origin (AR Rig)、平面检测） | 保留 |
| 作业功能 | 新增 `AssignmentController.cs` + `Assignment.unity` |

## 说明

- 碰撞检测用的是两个模型碰撞体包围盒（`Collider.bounds.Intersects`），并加了 1cm 容差方便实际操作。
- 屏幕 UI 用 OnGUI 绘制，中文通过运行时加载系统字体（PingFang SC 等）渲染；若某平台加载不到中文字体会回退到默认字体（中文可能显示为方块，不影响功能）。
- 立方体/球体用 Unity 内置图元 `GameObject.CreatePrimitive` 运行时创建，无需外部模型文件。
