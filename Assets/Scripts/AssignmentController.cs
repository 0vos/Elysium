using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using InputTouchPhase = UnityEngine.InputSystem.TouchPhase;

/// <summary>
/// 《虚拟现实技术》课程作业的独立 AR 交互控制器。
///
/// 交互方式（保留模板风格的“点击平面放置 + 手势控制”，但只有一个模型选择按钮）：
///   - 点击检测到的平面 → 放置当前选中的模型（立方体 / 球体）。
///   - 点击已放置的模型 → 选中并拖动移动。
///   - 双指捏合缩放、双指旋转。
///   - 两个模型相互靠近时触发碰撞检测（变绿 + 提示）。
///
/// 输入使用新的 Input System（EnhancedTouch + Mouse），因为本工程
/// activeInputHandler = Input System（纯新输入系统），旧的 Input.touch 等
/// API 不会收到任何事件。
///
/// 通过 [RuntimeInitializeOnLoadMethod] 运行时自动挂载，无需在场景里手动添加。
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

    // ============ 模型选择 ============
    enum ModelType { Cube, Sphere }
    ModelType m_CurrentModel = ModelType.Cube;

    // ============ 交互状态 ============
    GameObject m_Selected;   // 当前选中/拖动中的模型
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
    string m_Status = "Scan a surface, then tap it to place an object.";
    float m_StatusUntil;

    static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

    GUIStyle m_StatusStyle;
    GUIStyle m_ButtonStyle;
    GUIStyle m_HintStyle;

    // ============ 自动启动 ============
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<AssignmentController>() != null) return;

        var host = new GameObject("Course AR Controller");
        host.AddComponent<AssignmentController>();
        DontDestroyOnLoad(host);
    }

    void OnEnable()
    {
        // 启用增强触摸（新输入系统）
        EnhancedTouchSupport.Enable();
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
            m_Cube.transform.localScale = Vector3.one * 0.15f;
            m_CubeMaterial = MakeColoredMaterial(kCubeColor);
            m_Cube.GetComponent<Renderer>().sharedMaterial = m_CubeMaterial;
            m_Cube.SetActive(false);
        }

        if (m_Sphere == null)
        {
            m_Sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            m_Sphere.name = "SphereModel";
            m_Sphere.transform.localScale = Vector3.one * 0.15f;
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

    void Update()
    {
        DetectCollision();
        UpdateInput();
    }

    // ============ 输入（新 Input System） ============
    void UpdateInput()
    {
        var touches = Touch.activeTouches;

        // 双指：缩放 + 旋转
        if (touches.Count >= 2)
        {
            HandleTwoFinger(touches[0], touches[1]);
            return;
        }
        else if (m_TwoFinger)
        {
            m_TwoFinger = false;
            m_Selected = null;
            m_Dragging = false;
        }

        // 单指触摸
        if (touches.Count == 1)
        {
            HandleSingleTouch(touches[0]);
            return;
        }

        // 鼠标（编辑器，无触摸设备）
        var mouse = Mouse.current;
        if (mouse != null)
        {
            bool down = mouse.leftButton.wasPressedThisFrame;
            bool held = mouse.leftButton.isPressed;
            bool up = mouse.leftButton.wasReleasedThisFrame;
            if (down || held || up)
                HandlePointer(mouse.position.ReadValue(), down, held, up);
        }
    }

    void HandleSingleTouch(Touch t)
    {
        Vector2 pos = t.screenPosition;
        bool began = t.phase == InputTouchPhase.Began;
        bool held = t.phase == InputTouchPhase.Moved || t.phase == InputTouchPhase.Stationary;
        bool ended = t.phase == InputTouchPhase.Ended || t.phase == InputTouchPhase.Canceled;

        // 点在 UI 按钮区域时不当作场景交互（避免点按钮时误触放置）
        if (began && IsOverButton(pos)) return;

        HandlePointer(pos, began, held, ended);
    }

    // ============ 指针交互（自然模式） ============
    void HandlePointer(Vector2 pos, bool began, bool held, bool ended)
    {
        if (began)
        {
            // 先看是否点在已放置的模型上：是 → 选中并拖动
            GameObject hit = PickObject(pos);
            if (hit != null)
            {
                m_Selected = hit;
                m_Dragging = true;
                SetStatus("Object selected. Drag to move, pinch to scale, twist to rotate.");
                return;
            }

            // 否则点平面放置当前模型
            PlaceObject(pos);
        }
        else if (held && m_Selected != null)
        {
            MoveSelected(pos);
        }
        else if (ended)
        {
            m_Selected = null;
            m_Dragging = false;
        }
    }

    // ============ 放置模型 ============
    void PlaceObject(Vector2 pos)
    {
        GameObject target = m_CurrentModel == ModelType.Cube ? m_Cube : m_Sphere;
        if (target == null) { CreateModels(); target = m_CurrentModel == ModelType.Cube ? m_Cube : m_Sphere; }

        if (TryRaycastPlane(pos, out Pose pose))
        {
            target.transform.position = pose.position;
            target.transform.rotation = FaceUser(pose.rotation);
        }
        else
        {
            PlaceInFrontOfCamera(target);
        }

        target.SetActive(true);

        string name = m_CurrentModel == ModelType.Cube ? "cube" : "sphere";
        SetStatus("Placed a " + name + ". Tap empty ground to place another.");
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

        if (TryRaycastPlane(pos, out Pose pose))
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
    void HandleTwoFinger(Touch t0, Touch t1)
    {
        Vector2 p0 = t0.screenPosition;
        Vector2 p1 = t1.screenPosition;
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

    // ============ 碰撞检测（基于碰撞体包围盒相交） ============
    void DetectCollision()
    {
        if (m_Cube == null || m_Sphere == null) return;
        if (!m_Cube.activeSelf || !m_Sphere.activeSelf) return;

        var cb = m_Cube.GetComponent<Collider>();
        var sb = m_Sphere.GetComponent<Collider>();
        if (cb == null || sb == null) return;

        Bounds a = cb.bounds;
        a.Expand(0.01f); // 1cm 容差

        bool now = a.Intersects(sb.bounds);

        if (now && !m_WasColliding)
        {
            SetMaterialColor(m_CubeMaterial, kHitColor);
            SetMaterialColor(m_SphereMaterial, kHitColor);
            SetStatus("Collision detected! Both objects are highlighted green.");
        }
        else if (!now && m_WasColliding)
        {
            SetMaterialColor(m_CubeMaterial, kCubeColor);
            SetMaterialColor(m_SphereMaterial, kSphereColor);
            SetStatus("Objects separated. Move them together to collide again.");
        }

        m_WasColliding = now;
    }

    // ============ 模型选择 ============
    void SwitchModel()
    {
        m_CurrentModel = m_CurrentModel == ModelType.Cube ? ModelType.Sphere : ModelType.Cube;
        m_Selected = null;
        m_Dragging = false;
        string name = m_CurrentModel == ModelType.Cube ? "cube" : "sphere";
        SetStatus("Will place a " + name + ". Tap a surface to place it.");
    }

    void SetStatus(string text)
    {
        m_Status = text;
        m_StatusUntil = Time.time + 5f;
    }

    // ============ 屏幕 UI（OnGUI，一个模型选择按钮） ============
    float Scale()
    {
        return Mathf.Clamp(Screen.height / 1200f, 0.75f, 1.6f);
    }

    Rect ModelButtonRect(float s)
    {
        int bw = Mathf.RoundToInt(240 * s);
        int bh = Mathf.RoundToInt(64 * s);
        int x = (Screen.width - bw) / 2;
        int y = Screen.height - bh - Mathf.RoundToInt(30 * s);
        return new Rect(x, y, bw, bh);
    }

    bool IsOverButton(Vector2 screenPos)
    {
        // GUI 坐标原点在左上角，输入坐标原点在左下角，需翻转 Y
        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        return ModelButtonRect(Scale()).Contains(guiPos);
    }

    void OnGUI()
    {
        float s = Scale();
        EnsureStyles(s);

        // 顶部状态栏（半透明底）
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Mathf.RoundToInt(70 * s)), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(10, 10, Screen.width - 20, Mathf.RoundToInt(50 * s)), m_Status, m_StatusStyle);

        // 一个模型选择按钮（居中）
        Rect btn = ModelButtonRect(s);
        string label = m_CurrentModel == ModelType.Cube ? "MODEL: CUBE" : "MODEL: SPHERE";
        if (GUI.Button(btn, label, m_ButtonStyle))
            SwitchModel();

        // 底部操作提示
        GUI.Label(new Rect(10, btn.y - Mathf.RoundToInt(34 * s), Screen.width, Mathf.RoundToInt(28 * s)),
            "Tap surface: place    Tap object: drag    Pinch/twist: scale/rotate", m_HintStyle);
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

        m_StatusStyle.fontSize = Mathf.RoundToInt(22 * screenScale);
        m_ButtonStyle.fontSize = Mathf.RoundToInt(26 * screenScale);
        m_HintStyle.fontSize = Mathf.RoundToInt(18 * screenScale);
    }
}
