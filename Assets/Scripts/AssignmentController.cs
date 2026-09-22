using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// 《虚拟现实技术》课程作业的独立 AR 交互控制器。
///
/// 完成两项要求：
///   1. 放置 1 个模型，利用手势（拖动/缩放/旋转，编辑器内可用鼠标）做交互操作；
///   2. 放置 2 个模型，利用交互把它们相互靠近，触发碰撞检测并给出反馈。
///
/// The runtime UI is deliberately self-contained: it does not depend on or expose
/// any starter-scene controls. Labels use ASCII so they render on Android/iOS even
/// when a device does not provide a Chinese system font.
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
    string m_Status = "Scan a surface, then tap to place an object.";
    float m_StatusUntil;

    static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

    // IMGUI invokes OnGUI several times per frame. Cache styles rather than
    // allocating them on every layout and repaint event.
    GUIStyle m_StatusStyle;
    GUIStyle m_ButtonStyle;
    GUIStyle m_ModeStyle;

    // ============ 自动启动 ============
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        // The Assignment scene contains only the independent course UI. Avoid
        // scanning and changing every inactive scene object during app launch:
        // on a phone this can delay AR session startup, and it can accidentally
        // disable objects required by input or plane raycasts.
        if (FindFirstObjectByType<AssignmentController>() != null) return;

        var host = new GameObject("Course AR Controller");
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
            Debug.LogWarning("[Course AR] ARRaycastManager is missing; placement will use the camera fallback.");

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

    static void SetMaterialColor(Material material, Color color)
    {
        if (material == null) return;
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
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
                SetStatus("Object selected: drag to move; pinch or twist to transform.");
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

        bool hitPlane = TryRaycastPlane(pos, out Pose pose);

        if (hitPlane)
        {
            target.transform.position = pose.position;
            target.transform.rotation = FaceUser(pose.rotation);
        }
        else PlaceInFrontOfCamera(target);

        target.SetActive(true);

        string name = m_Mode == AppMode.PlaceCube ? "cube" : "sphere";
        SetStatus("Placed " + name + ". Place the other object, then choose INTERACT.");
    }

    bool TryRaycastPlane(Vector2 screenPosition, out Pose pose)
    {
        pose = default;
        if (m_RaycastManager == null) return false;

        s_Hits.Clear();
        // WithinBounds keeps placement responsive at the edge of a detected plane.
        if (!m_RaycastManager.Raycast(screenPosition, s_Hits,
                TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds))
            return false;

        pose = s_Hits[0].pose;
        return true;
    }

    void PlaceInFrontOfCamera(GameObject target)
    {
        if (m_Camera == null)
        {
            m_Camera = Camera.main;
            if (m_Camera == null) m_Camera = FindFirstObjectByType<Camera>();
        }

        if (m_Camera == null) return;
        target.transform.position = m_Camera.transform.position + m_Camera.transform.forward * 0.75f;
        target.transform.rotation = FaceUser(Quaternion.identity);
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

        bool hitPlane = TryRaycastPlane(pos, out Pose pose);

        if (hitPlane)
        {
            m_Selected.transform.position = pose.position;
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
            SetMaterialColor(m_CubeMaterial, kHitColor);
            SetMaterialColor(m_SphereMaterial, kHitColor);
            SetStatus("Collision detected! Both objects are highlighted green.");
        }
        else if (!now && m_WasColliding)
        {
            // 碰撞解除
            SetMaterialColor(m_CubeMaterial, kCubeColor);
            SetMaterialColor(m_SphereMaterial, kSphereColor);
            SetStatus("Objects separated. Move them together to detect another collision.");
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
            case AppMode.PlaceCube: SetStatus("PLACE CUBE: tap a detected surface."); break;
            case AppMode.PlaceSphere: SetStatus("PLACE SPHERE: tap a detected surface."); break;
            case AppMode.Interact: SetStatus("INTERACT: drag to move; pinch/twist to transform."); break;
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
        float s = Mathf.Clamp(Screen.height / 1200f, 0.75f, 1.6f);
        EnsureGuiStyles(s);

        // 顶部状态栏（半透明底）
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Mathf.RoundToInt(70 * s)), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 10, Screen.width - 20, Mathf.RoundToInt(50 * s)), m_Status, m_StatusStyle);

        // 底部模式按钮
        int bw = Mathf.RoundToInt(190 * s);
        int bh = Mathf.RoundToInt(64 * s);
        int gap = Mathf.RoundToInt(12 * s);
        int y = Screen.height - bh - Mathf.RoundToInt(24 * s);

        int x = Mathf.RoundToInt(12 * s);
        DrawModeButton(new Rect(x, y, bw, bh), "PLACE CUBE", AppMode.PlaceCube, m_ButtonStyle);
        x += bw + gap;
        DrawModeButton(new Rect(x, y, bw, bh), "PLACE SPHERE", AppMode.PlaceSphere, m_ButtonStyle);
        x += bw + gap;
        DrawModeButton(new Rect(x, y, bw, bh), "INTERACT", AppMode.Interact, m_ButtonStyle);

        // 当前模式高亮
        GUI.Label(new Rect(Mathf.RoundToInt(12 * s), y - Mathf.RoundToInt(34 * s), Screen.width, Mathf.RoundToInt(28 * s)),
            "MODE: " + ModeName(), m_ModeStyle);
    }

    void EnsureGuiStyles(float screenScale)
    {
        if (m_StatusStyle == null)
        {
            m_StatusStyle = new GUIStyle(GUI.skin.label);
            m_ButtonStyle = new GUIStyle(GUI.skin.button);
            m_ModeStyle = new GUIStyle(GUI.skin.label);
            m_StatusStyle.normal.textColor = Color.white;
            m_ModeStyle.normal.textColor = Color.yellow;
        }

        m_StatusStyle.fontSize = Mathf.RoundToInt(24 * screenScale);
        m_ButtonStyle.fontSize = Mathf.RoundToInt(24 * screenScale);
        m_ModeStyle.fontSize = Mathf.RoundToInt(20 * screenScale);
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
            case AppMode.PlaceCube: return "PLACE CUBE";
            case AppMode.PlaceSphere: return "PLACE SPHERE";
            default: return "INTERACT";
        }
    }
}
