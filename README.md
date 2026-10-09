# Elysium —《虚拟现实技术》课程作业（AR 交互 + 迷宫寻路）

基于 Unity 官方 **AR Mobile Template** 改写，包含两个作业场景：

1. **Assignment 场景** — 放置模型 + 手势交互 + 碰撞检测。
2. **Maze 场景** — 三维迷宫 + 路径搜索算法 + AR 可视化（球体自动寻路到终点）。

## 运行环境

- Unity **6000.3.10f1**（Unity 6）
- AR Foundation 6.3.3 + ARKit 6.3.3（iOS）/ ARCore 6.3.3（Android）
- XR Interaction Toolkit 3.3.1
- Universal Render Pipeline 17.3.0

## 场景一：Assignment（AR 交互 + 碰撞检测）

打开场景 `Assets/Scenes/Assignment.unity`。操作：

- 扫描地面/桌面，等检测到平面。
- 底部**模型选择按钮**在「立方体 / 球体」之间切换。
- **点平面放置**：轻点平面放当前模型（无平面时放到相机前方）。
- **手势控制**：轻点已放置的模型选中，单指拖动、双指缩放、双指旋转。
- 把立方体和球体拖到一起，两者变绿并提示碰撞检测成功。

## 场景二：Maze（迷宫寻路 + AR 可视化）

打开场景 `Assets/Scenes/Maze.unity`。这是一个在 AR 里生成的**三维迷宫**：橙色小球是角色，绿色小球是终点。

操作：

1. 扫描地面/桌面，**轻点平面放置迷宫**（无平面时放到相机前方）。
2. **轻点迷宫里任意可通行的格子**，小球会自动搜索路径并沿着路径移动到该处（该格成为新终点）。
3. 底部按钮切换算法，重复点击终点即可对比不同算法的搜索过程和路径。

支持的路径搜索算法：

| 算法 | 说明 |
| --- | --- |
| **BFS** | 广度优先，最少步数 |
| **Dijkstra** | 迪杰斯特拉，加权代价最短路径 |
| **A*** | 启发式搜索，扩展节点更少 |
| **DFS (ID)** | 深度优先（迭代加深 / 深度受限） |
| **Q-Learn** | Q-Learning 强化学习，学习出价值热力图后走贪心策略 |

可视化说明：算法搜索过程中访问过的格子会着色（Q-Learning 用「蓝→红」价值热力图），最终路径用青色线条标出。另外三个按钮：**Reset** 让小球回到起点、**New Maze** 重新生成迷宫、**Re-place** 重新把迷宫放到平面上。

## 文件结构

- `Assets/Scripts/AssignmentController.cs` — 场景一全部逻辑（放置、手势、碰撞），运行时自动挂载。
- `Assets/Scripts/Pathfinding/MazeGrid.cs` — 网格数据结构 + 递归回溯法迷宫生成（含环路与随机代价）。
- `Assets/Scripts/Pathfinding/Pathfinder.cs` — 路径搜索算法库：BFS / Dijkstra / A* / DFS(迭代加深) / Q-Learning。
- `Assets/Scripts/Pathfinding/MazePathfindingController.cs` — 场景二全部逻辑（三维迷宫构建、AR 放置、点击寻路、动画与可视化）。
- `Assets/Scenes/Assignment.unity`、`Assets/Scenes/Maze.unity` — 两个主场景（均在 Build Settings 中）。
- `Assets/MobileARTemplateAssets/`、`Assets/Samples/XR Interaction Toolkit/`、`Assets/XR/`、`Assets/XRI/`、`Assets/Settings/`、`Assets/TextMesh Pro/` — AR 平面可视化、XR Origin (AR Rig)、AR 加载器、URP 渲染配置与文字资源。
