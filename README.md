## 运行环境

- Unity **6000.3.10f1**（Unity 6）
- AR Foundation 6.3.3 + ARKit 6.3.3（iOS）
- XR Interaction Toolkit 3.3.1
- Universal Render Pipeline 17.3.0

## 怎么用

1. 用 Unity Hub 打开本项目，确认版本为 6000.3.x。
2. 打开场景 `Assets/Scenes/Assignment.unity`（已设为 Build Settings 唯一场景）。
3. 构建到 iPhone 运行（首次运行需授权相机，并配置签名与 Bundle ID）。

**操作说明：**

- 先用摄像头扫描地面/桌面，等待检测到平面。
- 底部有一个**模型选择按钮**，点它在「立方体 / 球体」之间切换。
- **点平面放置**：轻点扫描出的平面，把当前选中的模型放上去（物体底部贴平面）。
- **手势控制**：轻点已放置的模型选中它，单指拖动移动、双指捏合缩放、双指旋转。
- **碰撞检测**：把立方体和球体拖到一起，两者变绿并提示碰撞检测成功。

## 文件结构

- `Assets/Scripts/AssignmentController.cs` —— 任务全部逻辑（放置、手势交互、碰撞检测），通过 `[RuntimeInitializeOnLoadMethod]` 运行时自动挂载，无需在场景中手动添加。
- `Assets/Scenes/Assignment.unity` —— 主场景（AR Session + XR Origin (AR Rig) + 方向光 + EventSystem）。
- `Assets/MobileARTemplateAssets/` —— AR 平面可视化资源（遮挡平面 prefab、材质、着色器）。
- `Assets/Samples/XR Interaction Toolkit/` —— XR Origin (AR Rig) 等核心 prefab。
- `Assets/XR/`、`Assets/XRI/`、`Assets/Settings/`、`Assets/TextMesh Pro/` —— AR 加载器、XR 交互设置、URP 渲染配置与文字资源。
