using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 《虚拟现实技术》课程作业 —— 基于 AR Mobile 模板改写。
///
/// 完成两项要求：
///   1. 放置 1 个模型，利用手势（拖动/缩放/旋转，编辑器内可用鼠标）做交互操作；
///   2. 放置 2 个模型，利用交互把它们相互靠近，触发碰撞检测并给出反馈。
///
/// 使用说明：
///   - 下方三个按钮切换模式：放置立方体 / 放置球体 / 自由交互。
///   - 放置模式：轻点屏幕（或点击鼠标）把对应模型放到检测到的平面上；
///     若还没有检测到平面，会放到相机前方 0.5 米处。
///   - 自由交互模式：单指拖拽移动模型；双指捏合缩放、双指旋转。
///   - 当立方体与球体发生碰撞时，两者变绿并提示“碰撞检测成功”。
///
/// 本脚本通过 [RuntimeInitializeOnLoadMethod] 在运行时自动挂载，
/// 无需在场景里手动添加 GameObject。
/// </summary>
public class AssignmentController : MonoBehaviour
{
    // ============ 运行时引用 ============
    ARRaycastManager m_RaycastManager;
    ARPlaneManager m_PlaneManager;
    Camera m_Camera;

    // ============ 放置的两个模型 ============
    GameObject m_Cube;
    GameObject m_Sphere;
    Material m_CubeMaterial;
    Material m_SphereMaterial;

    static readonly Color kCubeColor = new Color(0.93f, 0.32f, 0.32f);
    static readonly Color kSphereColor = new Color(0.26f, 0.52f, 0.94f);
    static readonly Color kHitColor = new Color(0.2f, 0.85f, 0.35f);

    // ============ 交互状态 ============
    enum AppMode { PlaceCube, PlaceSphere, Interact }
    AppMode m_Mode = AppMode.PlaceCube;

    GameObject m_Selected;   // 当前选中的模型
    bool m_Dragging;

    // 双指手势（缩放 + 旋转）
    bool m_TwoFinger;
    float m_PinchStartDist;
    Vector3 m_PinchStartScale;
    float m_TwistStartAngle;
    Quaternion m_TwistStartRot;

    // ============ 碰撞检测 ============
    bool m_WasColliding;

    // ============ 状态提示 ============
    string m_Status = "请先扫描地面，然后点下方按钮放置模型";
    float m_StatusUntil;

    static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

    // ---------- 中文字体（运行时从系统加载，失败则退回默认字体） ----------
    static Font s_UIFont;
    static bool s_FontTried;

    static Font GetUIFont()
    {
        if (!s_FontTried)
        {
            s_FontTried = true;
            string[] candidates =
            {
                "PingFang SC", "Heiti SC", "STHeiti", "Hiragino Sans GB",
                "Microsoft YaHei", "Noto Sans CJK SC", "Source Han Sans SC",
            };
            foreach (var name in candidates)
            {
                try
                {
                    var f = Font.CreateDynamicFontFromOSFont(name, 24);
                    if (f != null) { s_UIFont = f; break; }
                }
                catch { /* 忽略并尝试下一个字体 */ }
            }
        }
        return s_UIFont;
    }

    // ============ 自动启动 ============
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // 防御性清理：若场景里还残留模板的示例 UI / 生成器，一并关掉。
        var all = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var go in all)
        {
            if (go == null) continue;
            string n = go.name;
            if (n == "UI" || n == "Object Spawner" || n == "GreetingCTA" ||
                n == "Coaching UI" || n.Contains("Prompt") || n.Contains("Menu") ||
                n.Contains("Debug"))
            {
                go.SetActive(false);
            }
        }

        var host = new GameObject("AssignmentController");
        host.AddComponent<AssignmentController>();
        DontDestroyOnLoad(host);
    }

    void Start()
    {
        m_Camera = Camera.main;
        if (m_Camera == null) m_Camera = FindFirstObjectByType<Camera>();
        m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();
        m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();

        if (m_RaycastManager == null)
            Debug.LogWarning("[Assignment] 未找到 ARRaycastManager，请确认场景中存在 XR Origin (AR Rig)。");

        CreateModels();
    }

    void CreateModels()
    {
        if (m_Cube == null)
        {
            m_Cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            m_Cube.name = "CubeModel";
            m_Cube.transform.localScale = Vector3.one * 0.12f;
            m_CubeMaterial = MakeColoredMaterial(kCubeColor);
            m_Cube.GetComponent<Renderer>().sharedMaterial = m_CubeMaterial;
            m_Cube.SetActive(false);
        }

        if (m_Sphere == null)
        {
            m_Sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_Sphere.name = "SphereModel";
            m_Sphere.transform.localScale = Vector3.one * 0.12f;
            m_SphereMaterial = MakeColoredMaterial(kSphereColor);
            m_Sphere.GetComponent<Renderer>().sharedMaterial = m_SphereMaterial;
            m_Sphere.SetActive(false);
        }
    }

    static Material MakeColoredMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        mat.color = color;                     // 兼容大多数着色器
        mat.SetColor("_BaseColor", color);     // URP Lit / Unlit
        mat.SetColor("_Color", color);
        return mat;
    }

    void Update()
    {
        DetectCollision();

        // 双指手势：缩放 + 旋转
        if (Input.touchCount == 2)
        {
            HandleTwoFinger();
            return;
        }
        else if (m_TwoFinger)
        {
            m_TwoFinger = false;
            m_Selected = null;
            m_Dragging = false;
        }

        // 单指触摸
        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);
            bool began = t.phase == TouchPhase.Began;
            bool held = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
            bool ended = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
            HandlePointer(t.position, began, held, ended);
            return;
        }

        // 鼠标（编辑器）
        bool mbDown = Input.GetMouseButtonDown(0);
        bool mb = Input.GetMouseButton(0);
        bool mbUp = Input.GetMouseButtonUp(0);
        if (mbDown || mb || mbUp)
            HandlePointer(Input.mousePosition, mbDown, mb, mbUp);
    }

    void HandlePointer(Vector2 pos, bool began, bool held, bool ended)
    {
        if (m_Mode != AppMode.Interact)
        {
            if (began) PlaceObject(pos);
            return;
        }

        // 自由交互模式
        if (began)
        {
            m_Selected = PickObject(pos);
            if (m_Selected != null)
            {
                m_Dragging = true;
                SetStatus("已选中模型：拖动移动，双指缩放/旋转");
            }
            else
            {
                m_Selected = null;
            }
        }
        else if (held && m_Selected != null)
        {
            MoveSelected(pos);
        }
        else if (ended && m_Selected != null)
        {
            m_Selected = null;
            m_Dragging = false;
        }
    }

    // ============ 放置模型 ============
    void PlaceObject(Vector2 pos)
    {
        GameObject target = m_Mode == AppMode.PlaceCube ? m_Cube : m_Sphere;
        if (target == null) { CreateModels(); target = m_Mode == AppMode.PlaceCube ? m_Cube : m_Sphere; }

        bool hitPlane = m_RaycastManager != null
            && m_RaycastManager.Raycast(pos, s_Hits, TrackableType.PlaneWithinPolygon);

        if (hitPlane)
        {
            Pose p = s_Hits[0].pose;
            target.transform.position = p.position;
            target.transform.rotation = FaceUser(p.rotation);
        }
        else if (m_Camera != null)
        {
            target.transform.position = m_Camera.transform.position + m_Camera.transform.forward * 0.5f;
            target.transform.rotation = FaceUser(Quaternion.identity);
        }

        target.SetActive(true);

        string name = m_Mode == AppMode.PlaceCube ? "立方体" : "球体";
        SetStatus("已放置" + name + "，可继续放置或切换到交互模式");
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

    // ============ 拾取与移动 ============
    GameObject PickObject(Vector2 pos)
    {
        if (m_Camera == null) return null;
        Ray ray = m_Camera.ScreenPointToRay(pos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            var go = hit.collider.gameObject;
            if (go == m_Cube || go == m_Sphere) return go;
        }
        return null;
    }

    void MoveSelected(Vector2 pos)
    {
        if (m_Selected == null) return;

        bool hitPlane = m_RaycastManager != null
            && m_RaycastManager.Raycast(pos, s_Hits, TrackableType.PlaneWithinPolygon);

        if (hitPlane)
        {
            m_Selected.transform.position = s_Hits[0].pose.position;
        }
        else if (m_Camera != null)
        {
            Plane p = new Plane(m_Camera.transform.forward, m_Selected.transform.position);
            Ray ray = m_Camera.ScreenPointToRay(pos);
            if (p.Raycast(ray, out float enter))
                m_Selected.transform.position = ray.GetPoint(enter);
        }
    }

    // ============ 双指：缩放 + 旋转 ============
    void HandleTwoFinger()
    {
        Touch t0 = Input.GetTouch(0);
        Touch t1 = Input.GetTouch(1);
        Vector2 p0 = t0.position;
        Vector2 p1 = t1.position;
        Vector2 dir = p1 - p0;
        float dist = dir.magnitude;

        if (m_Selected == null)
        {
            m_Selected = PickObject((p0 + p1) * 0.5f);
            if (m_Selected == null) { m_TwoFinger = false; return; }
        }

        if (!m_TwoFinger)
        {
            m_TwoFinger = true;
            m_PinchStartDist = dist;
            m_PinchStartScale = m_Selected.transform.localScale;
            m_TwistStartAngle = Vector2.SignedAngle(Vector2.right, dir);
            m_TwistStartRot = m_Selected.transform.rotation;
            return;
        }

        // 缩放
        if (m_PinchStartDist > 0.001f)
        {
            float f = Mathf.Clamp(dist / m_PinchStartDist, 0.3f, 3f);
            m_Selected.transform.localScale = m_PinchStartScale * f;
        }

        // 旋转（绕世界 Y 轴）
        float angle = Vector2.SignedAngle(Vector2.right, dir);
        float delta = Mathf.DeltaAngle(m_TwistStartAngle, angle);
        m_Selected.transform.rotation = m_TwistStartRot * Quaternion.Euler(0f, delta, 0f);
    }

    // ============ 碰撞检测（基于碰撞体的包围盒相交） ============
    void DetectCollision()
    {
        if (m_Cube == null || m_Sphere == null) return;
        if (!m_Cube.activeSelf || !m_Sphere.activeSelf) return;

        var cb = m_Cube.GetComponent<Collider>();
        var sb = m_Sphere.GetComponent<Collider>();
        if (cb == null || sb == null) return;

        Bounds a = cb.bounds;
        a.Expand(0.01f); // 1cm 容差，便于实际操作

        bool now = a.Intersects(sb.bounds);

        if (now && !m_WasColliding)
        {
            // 碰撞发生
            if (m_CubeMaterial != null) m_CubeMaterial.color = kHitColor;
            if (m_SphereMaterial != null) m_SphereMaterial.color = kHitColor;
            SetStatus("碰撞检测成功！立方体与球体发生碰撞");
        }
        else if (!now && m_WasColliding)
        {
            // 碰撞解除
            if (m_CubeMaterial != null) m_CubeMaterial.color = kCubeColor;
            if (m_SphereMaterial != null) m_SphereMaterial.color = kSphereColor;
            SetStatus("已分离，可再次靠近触发碰撞");
        }

        m_WasColliding = now;
    }

    // ============ 模式切换 ============
    void SetMode(AppMode mode)
    {
        m_Mode = mode;
        m_Selected = null;
        m_Dragging = false;
        switch (mode)
        {
            case AppMode.PlaceCube: SetStatus("放置立方体：轻点屏幕放置"); break;
            case AppMode.PlaceSphere: SetStatus("放置球体：轻点屏幕放置"); break;
            case AppMode.Interact: SetStatus("自由交互：拖动移动，双指缩放/旋转"); break;
        }
    }

    void SetStatus(string text)
    {
        m_Status = text;
        m_StatusUntil = Time.time + 5f;
    }

    // ============ 屏幕 UI ============
    void OnGUI()
    {
        Font font = GetUIFont();
        float s = Mathf.Clamp(Screen.height / 1200f, 0.75f, 1.6f);

        GUIStyle status = new GUIStyle(GUI.skin.label);
        if (font != null) status.font = font;
        status.fontSize = Mathf.RoundToInt(24 * s);
        status.normal.textColor = Color.white;

        // 顶部状态栏（半透明底）
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Mathf.RoundToInt(70 * s)), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 10, Screen.width - 20, Mathf.RoundToInt(50 * s)), m_Status, status);

        // 底部模式按钮
        int bw = Mathf.RoundToInt(190 * s);
        int bh = Mathf.RoundToInt(64 * s);
        int gap = Mathf.RoundToInt(12 * s);
        int y = Screen.height - bh - Mathf.RoundToInt(24 * s);

        GUIStyle btn = new GUIStyle(GUI.skin.button);
        if (font != null) btn.font = font;
        btn.fontSize = Mathf.RoundToInt(24 * s);

        int x = Mathf.RoundToInt(12 * s);
        DrawModeButton(new Rect(x, y, bw, bh), "放置立方体", AppMode.PlaceCube, btn);
        x += bw + gap;
        DrawModeButton(new Rect(x, y, bw, bh), "放置球体", AppMode.PlaceSphere, btn);
        x += bw + gap;
        DrawModeButton(new Rect(x, y, bw, bh), "自由交互", AppMode.Interact, btn);

        // 当前模式高亮
        GUIStyle modeLabel = new GUIStyle(GUI.skin.label);
        if (font != null) modeLabel.font = font;
        modeLabel.fontSize = Mathf.RoundToInt(20 * s);
        modeLabel.normal.textColor = Color.yellow;
        GUI.Label(new Rect(Mathf.RoundToInt(12 * s), y - Mathf.RoundToInt(34 * s), Screen.width, Mathf.RoundToInt(28 * s)),
            "当前模式：" + ModeName(), modeLabel);
    }

    void DrawModeButton(Rect r, string label, AppMode mode, GUIStyle style)
    {
        GUI.color = (m_Mode == mode) ? Color.green : Color.white;
        if (GUI.Button(r, label, style))
            SetMode(mode);
        GUI.color = Color.white;
    }

    string ModeName()
    {
        switch (m_Mode)
        {
            case AppMode.PlaceCube: return "放置立方体";
            case AppMode.PlaceSphere: return "放置球体";
            default: return "自由交互";
        }
    }
}
