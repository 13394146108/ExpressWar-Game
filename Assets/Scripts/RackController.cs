using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 货架：挂在货板上（需要一个 BoxCollider 当玩家触发区）。
/// ------------------------------------------------------------------
/// 槽位不需要任何物体标记，全部由参数算出来：
///   格数     = SlotCount
///   格宽     = SlotSpacing（世界米，同时决定快递占几格）
///   排布     = 以货板中心为基准，沿货板 right 方向左右对称等距排开
///   高度     = 货板顶面（自动取 Renderer 的 bounds 上沿）
/// 快递占几格 = Ceil(快递世界长度 / SlotSpacing)
///   小 0.2m / 中 0.4m / 大 0.6m → 1 / 2 / 3 格（SlotSpacing ≈ 0.28 时）
/// 尺寸全部在世界空间量（Renderer / Collider 的 bounds），
/// 所以货板带非均匀缩放、快递挂在哪个节点下，都不会影响判断。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RackController : MonoBehaviour
{
    private const string GrabTag = "CanGrab";
    private const float MinSpacing = 0.05f;

    [Header("槽位（世界米）")]
    [Tooltip("货架能摆几格")]
    public int SlotCount = 6;

    [Tooltip("每格的宽度，同时用于把快递长度换算成占格数")]
    [Range(0.05f, 0.5f)]
    public float SlotSpacing = 0.277f;

    [Header("摆放")]
    [Tooltip("勾选：快递跟随货架朝向；不勾：保持快递放下时的朝向")]
    public bool AlignToRackRotation = true;

    [Tooltip("摆好的快递挂在哪个节点下。留空 = 挂在货架自己下面")]
    public Transform ParcelRoot;

    [Header("预摆货物（做预制体时用）")]
    [Tooltip("启动时把 ParcelRoot 下已经摆好的快递登记成已占用槽位")]
    public bool RegisterPrePlaced = true;

    [Tooltip("登记时把预摆快递重新对齐到槽位（保证画面和占格数一致）")]
    public bool SnapPrePlaced = true;

    // 已摆放的快递，按放置顺序记录，从末尾（+X 端）按 Q 取下
    private class PlacedItem
    {
        public Rigidbody Body;
        public int SlotCount;
    }

    private readonly List<PlacedItem> placed = new List<PlacedItem>();

    // 接下来可用的第一个槽位下标（0 是最左端）
    private int usedSlots;

    // ---------- 对外状态 ----------
    public int Capacity => Mathf.Max(1, SlotCount);
    public int UsedSlots => usedSlots;
    public int RemainingSlots => Mathf.Max(0, Capacity - usedSlots);
    public int PlacedCount => placed.Count;

    /// <summary>快递实际挂到哪个节点下（默认挂在货架自己下面）</summary>
    private Transform ParcelParent => ParcelRoot != null ? ParcelRoot : transform;

    // ---------- 生命周期 ----------
    void Awake()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null) box.isTrigger = true;

        // 预制体里预先摆好的货物：登记进槽位账本，按 Q 才能正常取下来
        if (RegisterPrePlaced) RegisterPrePlacedItems();
    }

    // ---------- 槽位 ----------
    /// <summary>货板顶面的世界高度（槽位平面）</summary>
    private float TopY
    {
        get
        {
            Renderer r = GetComponent<Renderer>();
            if (r != null) return r.bounds.max.y;

            Collider c = GetComponent<Collider>();
            if (c != null) return c.bounds.max.y;

            return transform.position.y;
        }
    }

    /// <summary>第 index 个槽位的世界坐标（index = 0 是最左端）</summary>
    public Vector3 SlotWorld(int index)
    {
        Vector3 center = transform.position;
        center.y = TopY;

        // 以货板中心为基准左右对称排开
        float offset = (index - (Capacity - 1) * 0.5f) * SlotSpacing;
        return center + transform.right * offset;
    }

    // ---------- 触发区：把玩家登记到货架上 ----------
    void OnTriggerEnter(Collider other)
    {
        ThrowController tc = other.GetComponentInParent<ThrowController>();
        if (tc != null) tc.EnterRack(this);
    }

    void OnTriggerExit(Collider other)
    {
        ThrowController tc = other.GetComponentInParent<ThrowController>();
        if (tc != null) tc.ExitRack(this);
    }

    // ---------- 占位计算 ----------
    /// <summary>这件快递占几个槽位（由它自身的世界长度决定）</summary>
    public int SlotsFor(Rigidbody rb)
    {
        if (rb == null) return 0;

        float length = MeasureAlong(rb, transform.right);
        // 减一个极小量，避免 0.4/0.2 这类浮点误差被 Ceil 成 3
        int need = Mathf.CeilToInt(length / Mathf.Max(MinSpacing, SlotSpacing) - 0.0001f);
        return Mathf.Max(1, need);   // 不封顶：超过容量时 CanPlace 自然为 false
    }

    /// <summary>是否放得下：长度不超标 且 从最左端连续占用不越界</summary>
    public bool CanPlace(Rigidbody rb)
    {
        int need = SlotsFor(rb);
        return need > 0 && usedSlots + need <= Capacity;
    }

    // ---------- 放 / 取 ----------
    /// <summary>把一件快递摆到货架上，成功返回 true</summary>
    public bool Place(Rigidbody rb)
    {
        if (rb == null || !CanPlace(rb)) return false;

        int need = SlotsFor(rb);
        int first = usedSlots;
        int last = first + need - 1;

        // 水平位置 = 占用区间的中点；竖直方向沿世界 up 抬半个身高，底面贴着板面
        Vector3 center = (SlotWorld(first) + SlotWorld(last)) * 0.5f;
        center += Vector3.up * (MeasureAlong(rb, Vector3.up) * 0.5f);

        Quaternion rot = AlignToRackRotation ? transform.rotation : rb.transform.rotation;

        // 先定好世界变换再挂父物体（worldPositionStays=true 会保持世界缩放）
        rb.transform.SetPositionAndRotation(center, rot);
        rb.transform.SetParent(ParcelParent, true);

        Freeze(rb);

        usedSlots += need;
        placed.Add(new PlacedItem { Body = rb, SlotCount = need });
        return true;
    }

    /// <summary>从最后一格取下一件（后放的先取）。货架为空返回 null</summary>
    public Rigidbody TakeLast()
    {
        if (placed.Count == 0) return null;

        int last = placed.Count - 1;
        PlacedItem item = placed[last];
        placed.RemoveAt(last);
        usedSlots -= item.SlotCount;   // 归还槽位，下次放置会补回同位置

        item.Body.transform.SetParent(null, true);
        item.Body.isKinematic = false;
        item.Body.velocity = Vector3.zero;
        item.Body.angularVelocity = Vector3.zero;
        return item.Body;
    }

    // ---------- 预摆货物 ----------
    /// <summary>把预制体里已经摆好的快递登记成「已占用槽位」</summary>
    private void RegisterPrePlacedItems()
    {
        Transform root = ParcelParent;
        if (root == null) return;

        List<Rigidbody> items = new List<Rigidbody>();

        foreach (Rigidbody rb in root.GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == null || rb.transform == root) continue;
            if (!rb.CompareTag(GrabTag)) continue;                    // 只认可搬运的货物

            // 货物直接挂在别的货架（货板）下面时，不归自己管
            RackController owner = rb.GetComponentInParent<RackController>();
            if (owner != null && owner != this) continue;

            items.Add(rb);
        }

        // 沿货架 right 从左到右排序，占格顺序和视觉顺序一致
        items.Sort((a, b) => AlongRack(a.transform.position).CompareTo(AlongRack(b.transform.position)));

        foreach (Rigidbody rb in items)
        {
            if (SnapPrePlaced)
            {
                if (!Place(rb))
                    Debug.LogWarning($"[Rack] 预摆的 {rb.name} 放不下（货架共 {Capacity} 格），已跳过", this);
            }
            else
            {
                RegisterExisting(rb);
            }
        }
    }

    /// <summary>只登记、不挪位置（货物已经摆得刚好时用）</summary>
    private void RegisterExisting(Rigidbody rb)
    {
        int need = SlotsFor(rb);
        if (usedSlots + need > Capacity)
        {
            Debug.LogWarning($"[Rack] 预摆的 {rb.name} 超出容量（{usedSlots}+{need} > {Capacity}），已跳过", this);
            return;
        }

        Freeze(rb);
        usedSlots += need;
        placed.Add(new PlacedItem { Body = rb, SlotCount = need });
    }

    private float AlongRack(Vector3 worldPos) => Vector3.Dot(worldPos, transform.right);

    private static void Freeze(Rigidbody rb)
    {
        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // ---------- 测量（全部在世界空间，不受父级缩放影响） ----------
    /// <summary>快递沿某个世界方向的外接长度</summary>
    private static float MeasureAlong(Rigidbody rb, Vector3 worldDir)
    {
        if (rb == null) return 0f;

        Vector3 dir = worldDir.normalized;
        if (!TryGetWorldBounds(rb, out Bounds b)) return 0f;

        // 世界 AABB 沿该方向的投影长度
        return Mathf.Abs(b.size.x * dir.x) + Mathf.Abs(b.size.y * dir.y) + Mathf.Abs(b.size.z * dir.z);
    }

    private static bool TryGetWorldBounds(Rigidbody rb, out Bounds bounds)
    {
        bounds = default;
        bool has = false;

        // 优先用 Renderer：被拿在手上时碰撞体是关闭的，Renderer 一直有效
        foreach (Renderer r in rb.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled) continue;

            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }
        if (has) return true;

        foreach (Collider c in rb.GetComponentsInChildren<Collider>(true))
        {
            if (c == null || c.isTrigger) continue;

            if (!has) { bounds = c.bounds; has = true; }
            else bounds.Encapsulate(c.bounds);
        }

        return has;
    }

    // ---------- 调试 ----------
    void OnDrawGizmosSelected()
    {
        int count = Capacity;

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        for (int i = 0; i < count; i++)
            Gizmos.DrawWireSphere(SlotWorld(i), 0.03f);

        // 已占用的区间
        if (usedSlots > 0)
        {
            Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.9f);
            Gizmos.DrawLine(SlotWorld(0), SlotWorld(Mathf.Min(usedSlots, count) - 1));
        }
    }
}
