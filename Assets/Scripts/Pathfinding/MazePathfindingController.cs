using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// 迷宫寻路 AR 演示控制器（课程作业二：三维迷宫 + 路径搜索 + AR 可视化）。
///
/// 功能：
///   - 自动生成三维迷宫（递归回溯 + 环路），球体模型作为寻路角色。
///   - 设置终点（绿色小球），点击任意可通行格自动搜索路径并驱动球沿路径移动。
///   - 支持多种算法：BFS、Dijkstra、A*、DFS(迭代加深)、Q-Learning(强化学习)。
///   - 搜索过程可视化：已访问格子着色（RL 用价值热力图），路径用线条标出。
///
/// 输入沿用本工程的新 Input System（EnhancedTouch + Mouse）。
/// 通过 [RuntimeInitializeOnLoadMethod] 在 Maze 场景自动挂载（Assignment 场景不挂载）。
/// </summary>
public class MazePathfindingController : MonoBehaviour
{
    // ============ AR 引用 ============
    ARRaycastManager m_RaycastManager;
    Camera m_Camera;

    // ============ 迷宫 ============
    Grid m_Grid;
    Transform m_MazeRoot;
    GameObject m_Floor;
    readonly List<GameObject> m_Walls = new List<GameObject>();
    readonly List<GameObject> m_Markers = new List<GameObject>();

    const int kGridW = 11;
    const int kGridH = 11;
    const float kCell = 0.09f;      // 每格边长（米）
    const float kWallH = 0.07f;     // 墙高（米）
    const float kBallRadius = 0.03f;

    // ============ 角色与终点 ============
    GameObject m_Ball;
    GameObject m_GoalMarker;
    Vector2Int m_BallCell;
    Vector2Int m_GoalCell;

    // ============ 材质与路径线 ============
    Material m_BallMat;
    Material m_FloorMat;
    Material m_WallMat;
    Material m_GoalMat;
    Shader m_MarkerShader;
    LineRenderer m_PathLine;

    // ============ 算法与状态 ============
    PathAlgorithm m_Algorithm = PathAlgorithm.Dijkstra;
    bool m_MazePlaced;
    bool m_Animating;
    Coroutine m_AnimCoroutine;

    string m_Status = "Scan a surface, then tap it to place the maze.";
    float m_StatusUntil;

    static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

    // ============ UI ============
    GUIStyle m_StatusStyle, m_ButtonStyle, m_HintStyle;
    Rect m_ResetRect, m_NewMazeRect, m_ReposRect;
    Rect m_BfsRect, m_DijkRect, m_AStarRect, m_DfsRect, m_QlrRect;

    // ============ 自动启动（仅 Maze 场景） ============
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        bool isMaze = scene.name == "Maze" || scene.path.EndsWith("/Maze.unity");
        if (!isMaze) return;
        if (FindFirstObjectByType<MazePathfindingController>() != null) return;

        var host = new GameObject("Maze Pathfinding Controller");
        host.AddComponent<MazePathfindingController>();
    }

    void OnEnable() => EnhancedTouchSupport.Enable();

    void Start()
    {
        m_Camera = Camera.main;
        if (m_Camera == null) m_Camera = FindFirstObjectByType<Camera>();
        m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

        CreateMaterials();
        BuildMaze(42); // 固定种子，保证首次迷宫可复现
        m_MazeRoot.gameObject.SetActive(false); // 等待用户点击平面放置
    }

    void Update()
    {
        ComputeButtonRects();
        UpdateInput();
    }

    // ============ 构建迷宫 ============
    void CreateMaterials()
    {
        m_BallMat = MakeColoredMaterial(new Color(0.96f, 0.55f, 0.10f));  // 橙色球
        m_FloorMat = MakeColoredMaterial(new Color(0.90f, 0.90f, 0.93f)); // 浅灰地板
        m_WallMat = MakeColoredMaterial(new Color(0.24f, 0.27f, 0.42f));  // 深蓝墙
        m_GoalMat = MakeColoredMaterial(new Color(0.15f, 0.80f, 0.30f));  // 绿色终点

        m_MarkerShader = Shader.Find("Universal Render Pipeline/Lit");
        if (m_MarkerShader == null) m_MarkerShader = Shader.Find("Standard");
        if (m_MarkerShader == null) m_MarkerShader = Shader.Find("Sprites/Default");
    }

    void BuildMaze(int seed)
    {
        ClearMaze();
        m_Grid = MazeGenerator.Generate(kGridW, kGridH, seed);

        if (m_MazeRoot == null)
        {
            var root = new GameObject("Maze");
            m_MazeRoot = root.transform;
        }

        // 地板（一整块，含碰撞体用于拾取格子）
        m_Floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        m_Floor.name = "Floor";
        m_Floor.transform.SetParent(m_MazeRoot, false);
        m_Floor.transform.localScale = new Vector3(kGridW * kCell, 0.01f, kGridH * kCell);
        m_Floor.transform.localPosition = new Vector3(0f, -0.005f, 0f);
        m_Floor.GetComponent<Renderer>().sharedMaterial = m_FloorMat;

        // 墙（不可通行格）
        for (int x = 0; x < kGridW; x++)
            for (int y = 0; y < kGridH; y++)
            {
                if (m_Grid.walkable[x, y]) continue;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "Wall_" + x + "_" + y;
                wall.transform.SetParent(m_MazeRoot, false);
                wall.transform.localScale = new Vector3(kCell, kWallH, kCell);
                wall.transform.localPosition = CellToLocal(x, y) + new Vector3(0f, kWallH * 0.5f, 0f);
                wall.GetComponent<Renderer>().sharedMaterial = m_WallMat;
                wall.GetComponent<Collider>().enabled = false;
                m_Walls.Add(wall);
            }

        // 角色球（起点）
        if (m_Ball == null)
        {
            m_Ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_Ball.name = "Ball";
            m_Ball.GetComponent<Renderer>().sharedMaterial = m_BallMat;
            m_Ball.GetComponent<Collider>().enabled = false;
        }
        m_Ball.transform.SetParent(m_MazeRoot, false);
        m_Ball.transform.localScale = Vector3.one * (kBallRadius * 2f);

        // 终点标记（绿色小球）
        if (m_GoalMarker == null)
        {
            m_GoalMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_GoalMarker.name = "Goal";
            m_GoalMarker.GetComponent<Renderer>().sharedMaterial = m_GoalMat;
            m_GoalMarker.GetComponent<Collider>().enabled = false;
        }
        m_GoalMarker.transform.SetParent(m_MazeRoot, false);
        m_GoalMarker.transform.localScale = Vector3.one * (kBallRadius * 2f);

        m_BallCell = new Vector2Int(1, 1);
        m_GoalCell = new Vector2Int(kGridW - 2, kGridH - 2);
        PlaceBallAtCell(m_BallCell);
        PlaceGoalAtCell(m_GoalCell);

        EnsurePathLine();
        ClearPathVisual();
    }

    void ClearMaze()
    {
        if (m_Floor != null) Destroy(m_Floor);
        m_Floor = null;
        foreach (var w in m_Walls) if (w != null) Destroy(w);
        m_Walls.Clear();
        foreach (var mk in m_Markers) if (mk != null) Destroy(mk);
        m_Markers.Clear();
        if (m_PathLine != null) { Destroy(m_PathLine.gameObject); m_PathLine = null; }
        m_Grid = null;
    }

    Vector3 CellToLocal(int cx, int cy)
    {
        float x = (cx - (kGridW - 1) * 0.5f) * kCell;
        float z = (cy - (kGridH - 1) * 0.5f) * kCell;
        return new Vector3(x, 0f, z);
    }

    void PlaceBallAtCell(Vector2Int c)
    {
        m_Ball.transform.localPosition = CellToLocal(c.x, c.y) + new Vector3(0f, kBallRadius, 0f);
    }

    void PlaceGoalAtCell(Vector2Int c)
    {
        m_GoalMarker.transform.localPosition = CellToLocal(c.x, c.y) + new Vector3(0f, kBallRadius, 0f);
    }

    // ============ 输入 ============
    void UpdateInput()
    {
        var touches = Touch.activeTouches;
        if (touches.Count == 1 && touches[0].phase == InputTouchPhase.Began)
        {
            if (!IsOverAnyButton(touches[0].screenPosition))
                OnTap(touches[0].screenPosition);
            return;
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            if (!IsOverAnyButton(mouse.position.ReadValue()))
                OnTap(mouse.position.ReadValue());
        }
    }

    void OnTap(Vector2 pos)
    {
        if (!m_MazePlaced) { PlaceMazeAt(pos); return; }
        TrySetGoal(pos);
    }

    // ============ 放置迷宫 ============
    void PlaceMazeAt(Vector2 pos)
    {
        if (TryRaycastPlane(pos, out Pose pose))
        {
            m_MazeRoot.position = pose.position;
            m_MazeRoot.rotation = FaceUser(pose.rotation);
        }
        else if (m_Camera != null)
        {
            Vector3 p = m_Camera.transform.position + m_Camera.transform.forward * 0.85f;
            p.y = Mathf.Max(p.y, 0.02f);
            m_MazeRoot.position = p;
            m_MazeRoot.rotation = FaceUser(Quaternion.identity);
        }

        m_MazeRoot.gameObject.SetActive(true);
        m_MazePlaced = true;
        SetStatus("Maze placed. Tap any floor cell to move the ball there.");
    }

    bool TryRaycastPlane(Vector2 screenPosition, out Pose pose)
    {
        pose = default;
        if (m_RaycastManager == null) return false;
        s_Hits.Clear();
        if (!m_RaycastManager.Raycast(screenPosition, s_Hits,
                TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            return false;
        pose = s_Hits[0].pose;
        return true;
    }

    Quaternion FaceUser(Quaternion fallback)
    {
        if (m_Camera == null) return fallback;
        Vector3 fwd = m_Camera.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 0.001f)
            return Quaternion.LookRotation(fwd, Vector3.up);
        return fallback;
    }

    // ============ 点击设置终点并寻路 ============
    void TrySetGoal(Vector2 pos)
    {
        if (!PickMazeCell(pos, out Vector2Int cell)) return;
        if (!m_Grid.IsWalkable(cell)) { SetStatus("That's a wall — tap a floor cell."); return; }
        if (cell == m_BallCell) { SetStatus("Ball is already there."); return; }

        m_GoalCell = cell;
        PlaceGoalAtCell(cell);
        RunPathfinding();
    }

    bool PickMazeCell(Vector2 pos, out Vector2Int cell)
    {
        cell = default;
        if (m_Camera == null || m_Floor == null) return false;
        Ray ray = m_Camera.ScreenPointToRay(pos);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f)) return false;
        if (hit.collider.gameObject != m_Floor) return false;

        var local = m_MazeRoot.InverseTransformPoint(hit.point);
        int cx = Mathf.RoundToInt(local.x / kCell + (kGridW - 1) * 0.5f);
        int cy = Mathf.RoundToInt(local.z / kCell + (kGridH - 1) * 0.5f);
        if (cx < 0 || cx >= kGridW || cy < 0 || cy >= kGridH) return false;
        cell = new Vector2Int(cx, cy);
        return true;
    }

    // ============ 运行寻路 ============
    void RunPathfinding()
    {
        if (m_Animating && m_AnimCoroutine != null) StopCoroutine(m_AnimCoroutine);
        m_Animating = false;

        var res = Pathfinder.Solve(m_Grid, m_BallCell, m_GoalCell, m_Algorithm);
        SetStatus(res.note);

        ClearPathVisual();

        if (m_Algorithm == PathAlgorithm.QLearning && res.valueMap != null)
            ShowValueHeatmap(res.valueMap);
        else if (res.visited != null)
            ShowVisited(res.visited);

        DrawPath(res.path);

        if (res.found && res.path.Count >= 2)
            m_AnimCoroutine = StartCoroutine(AnimatePath(res.path));
    }

    void ShowVisited(List<Vector2Int> visited)
    {
        var seen = new HashSet<Vector2Int>();
        foreach (var c in visited)
        {
            if (!seen.Add(c)) continue;
            if (c == m_BallCell || c == m_GoalCell) continue;
            SpawnMarker(c, new Color(0.35f, 0.75f, 0.95f));
        }
    }

    void ShowValueHeatmap(float[,] valueMap)
    {
        float min = float.MaxValue, max = float.MinValue;
        for (int x = 0; x < kGridW; x++)
            for (int y = 0; y < kGridH; y++)
                if (m_Grid.IsWalkable(x, y))
                {
                    if (valueMap[x, y] < min) min = valueMap[x, y];
                    if (valueMap[x, y] > max) max = valueMap[x, y];
                }
        float range = (max - min) > 1e-4f ? max - min : 1f;

        for (int x = 0; x < kGridW; x++)
            for (int y = 0; y < kGridH; y++)
            {
                var c = new Vector2Int(x, y);
                if (!m_Grid.IsWalkable(c)) continue;
                if (c == m_BallCell || c == m_GoalCell) continue;
                float t = (valueMap[x, y] - min) / range;
                SpawnMarker(c, HeatColor(t));
            }
    }

    static Color HeatColor(float t)
    {
        // 蓝(低价值) → 青 → 绿 → 黄 → 红(高价值)
        return Color.HSVToRGB(Mathf.Lerp(0.65f, 0f, Mathf.Clamp01(t)), 0.9f, 1f);
    }

    void SpawnMarker(Vector2Int c, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Marker";
        go.transform.SetParent(m_MazeRoot, false);
        go.transform.localScale = new Vector3(kCell * 0.84f, 0.012f, kCell * 0.84f);
        go.transform.localPosition = CellToLocal(c.x, c.y) + new Vector3(0f, 0.006f, 0f);
        var collider = go.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
        var mat = new Material(m_MarkerShader);
        SetMaterialColor(mat, color);
        go.GetComponent<Renderer>().sharedMaterial = mat;
        m_Markers.Add(go);
    }

    void DrawPath(List<Vector2Int> path)
    {
        if (path == null || path.Count == 0) return;
        EnsurePathLine();
        m_PathLine.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++)
            m_PathLine.SetPosition(i, CellToLocal(path[i].x, path[i].y) + new Vector3(0f, 0.014f, 0f));
        m_PathLine.enabled = true;
    }

    void EnsurePathLine()
    {
        if (m_PathLine != null) return;
        var go = new GameObject("PathLine");
        go.transform.SetParent(m_MazeRoot, false);
        m_PathLine = go.AddComponent<LineRenderer>();
        m_PathLine.useWorldSpace = false;
        m_PathLine.widthMultiplier = 0.008f;
        m_PathLine.numCapVertices = 4;
        m_PathLine.material = new Material(Shader.Find("Sprites/Default"));
        m_PathLine.startColor = new Color(0f, 0.9f, 0.9f, 1f);
        m_PathLine.endColor = new Color(0f, 0.9f, 0.9f, 1f);
        m_PathLine.enabled = false;
    }

    void ClearPathVisual()
    {
        foreach (var mk in m_Markers) if (mk != null) Destroy(mk);
        m_Markers.Clear();
        if (m_PathLine != null) m_PathLine.enabled = false;
    }

    // ============ 动画 ============
    IEnumerator AnimatePath(List<Vector2Int> path)
    {
        m_Animating = true;
        foreach (var c in path)
        {
            Vector3 target = CellToLocal(c.x, c.y) + new Vector3(0f, kBallRadius, 0f);
            Vector3 startPos = m_Ball.transform.localPosition;
            float t = 0f;
            const float dur = 0.18f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                float k = Mathf.Clamp01(t);
                Vector3 p = Vector3.Lerp(startPos, target, k);
                p.y += Mathf.Sin(k * Mathf.PI) * kBallRadius * 0.5f; // 轻微弹跳
                m_Ball.transform.localPosition = p;
                yield return null;
            }
            m_Ball.transform.localPosition = target;
            m_BallCell = c;
        }
        m_Animating = false;
        SetStatus("Arrived at goal. Tap another cell to path again.");
    }

    // ============ 按钮动作 ============
    void ResetBall()
    {
        if (m_Animating && m_AnimCoroutine != null) StopCoroutine(m_AnimCoroutine);
        m_Animating = false;
        m_BallCell = new Vector2Int(1, 1);
        m_GoalCell = new Vector2Int(kGridW - 2, kGridH - 2);
        PlaceBallAtCell(m_BallCell);
        PlaceGoalAtCell(m_GoalCell);
        ClearPathVisual();
        SetStatus("Reset. Tap a floor cell to move the ball.");
    }

    void NewMaze()
    {
        if (m_Animating && m_AnimCoroutine != null) StopCoroutine(m_AnimCoroutine);
        m_Animating = false;
        BuildMaze(System.Environment.TickCount);
        SetStatus("New maze generated. Tap a floor cell to move the ball.");
    }

    void Reposition()
    {
        if (m_Animating && m_AnimCoroutine != null) StopCoroutine(m_AnimCoroutine);
        m_Animating = false;
        m_MazeRoot.gameObject.SetActive(false);
        m_MazePlaced = false;
        SetStatus("Tap a surface to place the maze.");
    }

    void SetStatus(string text)
    {
        m_Status = text;
        m_StatusUntil = Time.time + 6f;
    }

    // ============ 材质工具 ============
    static Material MakeColoredMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        SetMaterialColor(mat, color);
        return mat;
    }

    static void SetMaterialColor(Material material, Color color)
    {
        if (material == null) return;
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
    }

    // ============ UI ============
    float Scale() => Mathf.Clamp(Screen.height / 1200f, 0.75f, 1.6f);

    void ComputeButtonRects()
    {
        float s = Scale();
        float bh = 46f * s;
        float gap = 8f * s;
        float margin = 10f;
        float aw = (Screen.width - 2f * margin - 2f * gap) / 3f;
        float y = Screen.height - margin - bh;

        m_ResetRect = new Rect(margin, y, aw, bh);
        m_NewMazeRect = new Rect(margin + aw + gap, y, aw, bh);
        m_ReposRect = new Rect(margin + 2f * (aw + gap), y, aw, bh);

        float yAlgo2 = y - gap - bh;
        float yAlgo1 = yAlgo2 - gap - bh;
        m_BfsRect = new Rect(margin, yAlgo1, aw, bh);
        m_DijkRect = new Rect(margin + aw + gap, yAlgo1, aw, bh);
        m_AStarRect = new Rect(margin + 2f * (aw + gap), yAlgo1, aw, bh);

        float aw2 = (Screen.width - 2f * margin - gap) / 2f;
        m_DfsRect = new Rect(margin, yAlgo2, aw2, bh);
        m_QlrRect = new Rect(margin + aw2 + gap, yAlgo2, aw2, bh);
    }

    bool IsOverAnyButton(Vector2 screenPos)
    {
        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        return m_ResetRect.Contains(guiPos) || m_NewMazeRect.Contains(guiPos) || m_ReposRect.Contains(guiPos)
            || m_BfsRect.Contains(guiPos) || m_DijkRect.Contains(guiPos) || m_AStarRect.Contains(guiPos)
            || m_DfsRect.Contains(guiPos) || m_QlrRect.Contains(guiPos);
    }

    void OnGUI()
    {
        float s = Scale();
        EnsureStyles(s);

        // 顶部状态栏
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Mathf.RoundToInt(78f * s)), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 8, Screen.width - 20, Mathf.RoundToInt(62f * s)), m_Status, m_StatusStyle);

        // 动作按钮
        if (GUI.Button(m_ResetRect, "Reset", m_ButtonStyle)) ResetBall();
        if (GUI.Button(m_NewMazeRect, "New Maze", m_ButtonStyle)) NewMaze();
        if (GUI.Button(m_ReposRect, "Re-place", m_ButtonStyle)) Reposition();

        // 算法按钮
        DrawAlgoButton(m_BfsRect, PathAlgorithm.BFS, "BFS");
        DrawAlgoButton(m_DijkRect, PathAlgorithm.Dijkstra, "Dijkstra");
        DrawAlgoButton(m_AStarRect, PathAlgorithm.AStar, "A*");
        DrawAlgoButton(m_DfsRect, PathAlgorithm.DFS, "DFS (ID)");
        DrawAlgoButton(m_QlrRect, PathAlgorithm.QLearning, "Q-Learn");

        // 提示
        GUI.Label(new Rect(10, m_BfsRect.y - Mathf.RoundToInt(26f * s), Screen.width, Mathf.RoundToInt(22f * s)),
            "Tap floor: set goal & move    Orange ball = start    Green ball = goal", m_HintStyle);
    }

    void DrawAlgoButton(Rect r, PathAlgorithm algo, string label)
    {
        bool active = m_Algorithm == algo;
        var old = GUI.backgroundColor;
        GUI.backgroundColor = active ? new Color(0.25f, 0.6f, 1f) : Color.white;
        if (GUI.Button(r, label, m_ButtonStyle))
        {
            m_Algorithm = algo;
            SetStatus("Algorithm: " + label + ". Tap a floor cell to move the ball.");
        }
        GUI.backgroundColor = old;
    }

    void EnsureStyles(float screenScale)
    {
        if (m_StatusStyle == null)
        {
            m_StatusStyle = new GUIStyle(GUI.skin.label);
            m_ButtonStyle = new GUIStyle(GUI.skin.button);
            m_HintStyle = new GUIStyle(GUI.skin.label);
            m_StatusStyle.normal.textColor = Color.white;
            m_HintStyle.normal.textColor = Color.yellow;
            m_StatusStyle.alignment = TextAnchor.MiddleLeft;
            m_HintStyle.alignment = TextAnchor.MiddleCenter;
        }
        m_StatusStyle.fontSize = Mathf.RoundToInt(20 * screenScale);
        m_ButtonStyle.fontSize = Mathf.RoundToInt(17 * screenScale);
        m_HintStyle.fontSize = Mathf.RoundToInt(14 * screenScale);
    }
}
