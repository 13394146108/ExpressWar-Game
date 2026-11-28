using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 货架：挂在货板上（需要一个 BoxCollider 当玩家触发区）。
/// ------------------------------------------------------------------
/// 容量按「单位长度」算：一块货板 = SlotCount 个单位（默认 6），
/// 快递占几个单位看 GrabbableItem 的尺寸：小 = 1 / 中 = 2 / 大 = 3。
/// 所以 3 个中（2+2+2=6）正好塞满，再也塞不进第 4 个或别的快递。
///
/// 放置固定从长轴负向（第 0 格）往正向依次找第一段够放的空档，
/// 和玩家站位、放置先后无关：
///   放在哪 = 从负向数起第一段够放的空档
///   拿哪件 = 离玩家站位最近的那件，而不是最后放的那件
/// 快递两头都按整格对齐（占用区间居中），所以格子和格子之间不会重叠。
/// 尺寸全部在世界空间量（Renderer / Collider 的 bounds），
/// 所以货板带非均匀缩放、快递挂在哪个节点下，都不会影响判断。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RackController : MonoBehaviour
{
    private const string GrabTag = "CanGrab";
    private const float MinSpacing = 0.05f;

    [Header("容量（单位长度）")]
    [Tooltip("一块货板能放几个单位：小快递 1 / 中 2 / 大 3，默认 6 个单位")]
    public int SlotCount = 6;

    [Tooltip("一个单位有多宽（世界米）。关掉 AutoFit 时用它；取不到货板尺寸时也用它兜底")]
    [Range(0.05f, 0.5f)]
    public float SlotSpacing = 0.277f;

    [Tooltip("按货板实际宽度自动算单位宽度：让 SlotCount 个单位正好铺满货板")]
    public bool AutoFitUnitsToBoard = true;

    [Tooltip("两端各留出的空隙（只在 AutoFit 时生效）")]
    [Min(0f)]
    public float EdgePadding = 0.02f;

    [Header("摆放")]
    [Tooltip("勾选：快递跟随货架朝向；不勾：保持快递放下时的朝向")]
    public bool AlignToRackRotation = true;

    [Tooltip("摆好的快递挂在哪个节点下。留空 = 挂在货架自己下面")]
    public Transform ParcelRoot;

    [Header("预摆货物（做预制体时用）")]
    [Tooltip("启动时把 ParcelRoot 下已经摆好的快递登记成已占用空间")]
    public bool RegisterPrePlaced = true;

    [Tooltip("登记时把预摆快递重新对齐到格子（只挪动没对齐的那些）")]
    public bool SnapPrePlaced = true;

    // 已摆放的快递：记的是「从第几格开始、占几格」，和放置顺序无关
    private class PlacedItem
    {
        public Rigidbody Body;
        public int First;    // 起始格下标（0 = 最左端）
        public int Units;    // 占几格

        public int Last => First + Units - 1;
    }

    private readonly List<PlacedItem> placed = new List<PlacedItem>();

    /// <summary>给外部功能（合成、计分等）看的只读快照</summary>
    public struct PlacedInfo
    {
        public Rigidbody Body;
        public int First;   // 起始格下标
        public int Units;   // 占几格

        public int Last => First + Units - 1;
    }

    /// <summary>货架上的摆放发生变化时触发（放上、拿走、摘除），参数是这块货架</summary>
    public event System.Action<RackController> PlacedChanged;

    // ---------- 对外状态 ----------
    public int Capacity => Mathf.Max(1, SlotCount);
    public int PlacedCount => placed.Count;

    /// <summary>已占用的单位数</summary>
    public int UsedSlots
    {
        get
        {
            int n = 0;
            for (int i = 0; i < placed.Count; i++)
                if (placed[i].Body != null) n += placed[i].Units;

            return n;
        }
    }

    public int RemainingSlots => Mathf.Max(0, Capacity - UsedSlots);

    /// <summary>快递实际挂到哪个节点下（默认挂在货架自己下面）</summary>
    private Transform ParcelParent => ParcelRoot != null ? ParcelRoot : transform;

    // ---------- 生命周期 ----------
    void Awake()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null) box.isTrigger = true;

        // 预制体里预先摆好的货物：登记进格子账本，按键才能正常拿下来
        if (RegisterPrePlaced) RegisterPrePlacedItems();
    }

    // ---------- 格子 ----------
    /// <summary>货板顶面的世界高度（摆放平面）</summary>
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

    /// <summary>落位基准点：货板中心的那一点（y 取板面）</summary>
    private Vector3 BoardCenter
    {
        get
        {
            Vector3 p = transform.position;
            p.y = TopY;
            return p;
        }
    }

    /// <summary>货板沿长轴的实际宽度（世界米）</summary>
    public float SpaceWidth
    {
        get
        {
            Vector3 axis = transform.right;

            Renderer r = GetComponent<Renderer>();
            if (r != null)
            {
                float w = ProjectLength(r.bounds, axis);
                if (w > MinSpacing) return w;
            }

            Collider c = GetComponent<Collider>();
            if (c != null)
            {
                float w = ProjectLength(c.bounds, axis);
                if (w > MinSpacing) return w;
            }

            return Capacity * Mathf.Max(MinSpacing, SlotSpacing);
        }
    }

    /// <summary>一个单位有多宽（世界米）：默认让 Capacity 个单位正好铺满货板</summary>
    public float UnitWidth
    {
        get
        {
            float fallback = Mathf.Max(MinSpacing, SlotSpacing);
            if (!AutoFitUnitsToBoard) return fallback;

            float usable = SpaceWidth - 2f * Mathf.Max(0f, EdgePadding);
            float w = usable / Capacity;
            return w > MinSpacing ? w : fallback;
        }
    }

    /// <summary>第 index 格中心相对货板中心的偏移（米）</summary>
    private float SlotOffset(int index) =>
        (index - (Capacity - 1) * 0.5f) * UnitWidth;

    /// <summary>第 edgeIndex 条格线相对货板中心的偏移（0 = 最左边缘，Capacity = 最右边缘）</summary>
    private float EdgeOffset(int edgeIndex) =>
        (edgeIndex - Capacity * 0.5f) * UnitWidth;

    /// <summary>第 index 格中心的世界坐标</summary>
    public Vector3 SlotCenter(int index) =>
        BoardCenter + transform.right * SlotOffset(index);

    /// <summary>某个世界点投影到货架长轴上的位置（相对货板中心，米）</summary>
    public float AlongRack(Vector3 worldPos) =>
        Vector3.Dot(worldPos - BoardCenter, transform.right);

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

    // ---------- 占几格 ----------
    /// <summary>
    /// 这件快递占几个单位：以 GrabbableItem 的尺寸为准（小 1 / 中 2 / 大 3）。
    /// 如果快递做得比标定的尺寸还宽，就按实际宽度占格，避免视觉上互相穿模。
    /// </summary>
    public int UnitsFor(Rigidbody rb)
    {
        if (rb == null) return 0;

        int bySize = 0;
        GrabbableItem item = rb.GetComponentInParent<GrabbableItem>();
        if (item != null) bySize = Mathf.Max(1, item.Weight);

        float length = LengthWhenPlaced(rb);
        int byLength = Mathf.CeilToInt(length / Mathf.Max(MinSpacing, UnitWidth) - 0.0001f);

        return Mathf.Max(1, Mathf.Max(bySize, byLength));
    }

    /// <summary>
    /// 能不能放：从长轴负向往正向找，是否存在一段够放的空档。
    /// fromWorldPosition 仅为兼容旧调用保留，不再影响落点。
    /// </summary>
    public bool CanPlace(Rigidbody rb, Vector3 fromWorldPosition) =>
        TryResolveSlot(rb, out _, out _);

    // ---------- 放 ----------
    /// <summary>把一件快递摆到货架上（从负向数起第一处够放的空档），成功返回 true</summary>
    public bool Place(Rigidbody rb, Vector3 fromWorldPosition)
    {
        if (!TryResolveSlot(rb, out int first, out int units)) return false;

        ApplyPlacement(rb, first, units);
        return true;
    }

    /// <summary>预摆货物专用：按货物自身位置就近对齐，不走顺序放置</summary>
    private bool PlaceNear(Rigidbody rb, Vector3 fromWorldPosition)
    {
        if (!TryResolveSlotNear(rb, fromWorldPosition, out int first, out int units)) return false;

        ApplyPlacement(rb, first, units);
        return true;
    }

    /// <summary>
    /// 把手上的最上层快递放到货架上。
    /// 先在手里算好落点，确认放得下才真正从手里取走 —— 失败时快递原样留在手上。
    /// </summary>
    public bool TryPlaceFromHands(GrabController hands, Vector3 fromWorldPosition)
    {
        if (hands == null) return false;

        Rigidbody top = hands.TopBody;
        if (!TryResolveSlot(top, out int first, out int units)) return false;

        Rigidbody rb = hands.Release();      // 从最上层取走
        if (rb == null) return false;

        ApplyPlacement(rb, first, units);
        return true;
    }

    /// <summary>预览：这件快递按负向到正向的顺序会落到哪（世界中心点 + 占用宽度）</summary>
    public bool TryPreviewSpot(Rigidbody rb, Vector3 fromWorldPosition,
                               out Vector3 worldCenter, out float length)
    {
        worldCenter = Vector3.zero;
        length = 0f;

        if (!TryResolveSlot(rb, out int first, out int units)) return false;

        float offset = (SlotOffset(first) + SlotOffset(first + units - 1)) * 0.5f;
        float height = MeasureAlong(rb, Vector3.up);

        worldCenter = BoardCenter + transform.right * offset + Vector3.up * (height * 0.5f);
        length = units * UnitWidth;
        return true;
    }

    /// <summary>
    /// 放置落点：从长轴负向（第 0 格）往正向扫，取第一段够放的空档。
    /// 和玩家站位无关；从手里放东西、预览都走这里。
    /// </summary>
    private bool TryResolveSlot(Rigidbody rb, out int first, out int units)
    {
        first = 0;
        units = 0;
        if (rb == null) return false;

        units = UnitsFor(rb);
        int cap = Capacity;
        if (units <= 0 || units > cap) return false;    // 比整块板还宽

        for (int i = 0; i <= cap - units; i++)
        {
            if (!AreSlotsFree(i, units)) continue;

            first = i;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 按世界位置找最近落点：只给预摆货物登记用，保持原有的就近对齐行为。
    /// </summary>
    private bool TryResolveSlotNear(Rigidbody rb, Vector3 fromWorldPosition,
                                    out int first, out int units)
    {
        first = 0;
        units = 0;
        if (rb == null) return false;

        units = UnitsFor(rb);
        int cap = Capacity;
        if (units <= 0 || units > cap) return false;

        int want = WantedFirstSlot(fromWorldPosition, units);

        int best = -1;
        int bestDist = int.MaxValue;

        for (int i = 0; i <= cap - units; i++)
        {
            if (!AreSlotsFree(i, units)) continue;

            int d = Mathf.Abs(i - want);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        if (best < 0) return false;

        first = best;
        return true;
    }

    /// <summary>给定世界位置对应的起始格：让占的格区间中心尽量正对那个位置（预摆登记用）</summary>
    private int WantedFirstSlot(Vector3 fromWorldPosition, int units)
    {
        float index = AlongRack(fromWorldPosition) / UnitWidth + (Capacity - 1) * 0.5f;
        int first = Mathf.RoundToInt(index - (units - 1) * 0.5f);

        return Mathf.Clamp(first, 0, Capacity - units);
    }

    /// <summary>[first, first+units) 这几格是不是都空着</summary>
    private bool AreSlotsFree(int first, int units)
    {
        for (int i = 0; i < placed.Count; i++)
        {
            PlacedItem it = placed[i];
            if (it.Body == null) continue;

            if (first <= it.Last && it.First < first + units) return false;
        }
        return true;
    }

    private void ApplyPlacement(Rigidbody rb, int first, int units)
    {
        Quaternion rot = AlignToRackRotation ? transform.rotation : rb.transform.rotation;

        rb.transform.rotation = rot;                        // 先摆正，高度才量得准
        float height = MeasureAlong(rb, Vector3.up);

        // 水平位置 = 占用格区间的中点；竖直方向抬半个身高，底面贴着板面
        float offset = (SlotOffset(first) + SlotOffset(first + units - 1)) * 0.5f;
        Vector3 center = BoardCenter + transform.right * offset + Vector3.up * (height * 0.5f);

        // 先定好世界变换再挂父物体（worldPositionStays=true 会保持世界缩放）
        rb.transform.SetPositionAndRotation(center, rot);
        rb.transform.SetParent(ParcelParent, true);

        Freeze(rb);

        RemoveRecord(rb);                                   // 同一件重复登记时先清旧记录
        placed.Add(new PlacedItem { Body = rb, First = first, Units = units });

        PlacedChanged?.Invoke(this);
    }

    // ---------- 取 ----------
    /// <summary>离玩家站位最近的那件快递（看它占的格区间离站位多远）</summary>
    private PlacedItem FindNearest(Vector3 fromWorldPosition)
    {
        if (placed.Count == 0) return null;

        float along = AlongRack(fromWorldPosition);
        float halfUnit = UnitWidth * 0.5f;

        PlacedItem best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < placed.Count; i++)
        {
            PlacedItem it = placed[i];
            if (it.Body == null) continue;

            float a = SlotOffset(it.First) - halfUnit;
            float b = SlotOffset(it.Last) + halfUnit;

            float d = 0f;
            if (along < a) d = a - along;
            else if (along > b) d = along - b;

            if (d < bestDist)
            {
                bestDist = d;
                best = it;
            }
        }

        return best;
    }

    /// <summary>
    /// 把离玩家站位最近的那件直接拿进手里。
    /// 装不下 / 已拿着 / 被别人拿着时返回 false，且不改动任何状态。
    /// </summary>
    public bool TryTakeToHands(GrabController hands, Vector3 fromWorldPosition)
    {
        PlacedItem item = FindNearest(fromWorldPosition);
        if (item == null) return false;

        if (!hands.TryAddToHands(item.Body)) return false;   // 超重 / 叠满：原样留在货架上

        placed.Remove(item);
        PlacedChanged?.Invoke(this);
        return true;
    }

    // ---------- 给「合成」这类功能用的接口 ----------
    /// <summary>取一份当前摆放情况（按起始格从左到右排好）</summary>
    public void GetPlacedItems(List<PlacedInfo> results)
    {
        if (results == null) return;
        results.Clear();

        for (int i = 0; i < placed.Count; i++)
        {
            PlacedItem it = placed[i];
            if (it.Body == null) continue;

            results.Add(new PlacedInfo { Body = it.Body, First = it.First, Units = it.Units });
        }

        results.Sort((a, b) => a.First.CompareTo(b.First));
    }

    /// <summary>把一件快递从账本上摘掉（不动它的位置和物理状态），供合成时消耗掉原来的快递</summary>
    public bool RemovePlaced(Rigidbody rb)
    {
        if (rb == null) return false;

        for (int i = 0; i < placed.Count; i++)
        {
            if (placed[i].Body != rb) continue;

            placed.RemoveAt(i);
            PlacedChanged?.Invoke(this);
            return true;
        }
        return false;
    }

    /// <summary>把一件快递摆到指定的起始格（合成出来的大快递用），占几格按它自己的尺寸算</summary>
    public bool PlaceAt(Rigidbody rb, int first)
    {
        if (rb == null) return false;

        int units = UnitsFor(rb);
        int cap = Capacity;
        if (units <= 0 || units > cap) return false;

        first = Mathf.Clamp(first, 0, cap - units);
        if (!AreSlotsFree(first, units)) return false;

        ApplyPlacement(rb, first, units);
        return true;
    }

    // ---------- 预摆货物 ----------
    /// <summary>把预制体里已经摆好的快递登记成「已占用格子」</summary>
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

        // 按实际位置从左到右登记，先来后到只影响重叠时谁被跳过
        items.Sort((a, b) => AlongRack(a.transform.position).CompareTo(AlongRack(b.transform.position)));

        foreach (Rigidbody rb in items)
        {
            if (SnapPrePlaced)
            {
                if (!PlaceNear(rb, rb.transform.position))
                    Debug.LogWarning($"[Rack] 预摆的 {rb.name} 在这块货板上找不到空位，已跳过", this);
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
        int units = UnitsFor(rb);              // 保持原朝向量
        int cap = Capacity;

        if (units <= 0 || units > cap)
        {
            Debug.LogWarning($"[Rack] 预摆的 {rb.name} 比整块货板还大（{units} > {cap} 格），已跳过", this);
            return;
        }

        int first = WantedFirstSlot(rb.transform.position, units);

        if (!AreSlotsFree(first, units))
        {
            Debug.LogWarning($"[Rack] 预摆的 {rb.name} 和别的快递占同一格，已跳过", this);
            return;
        }

        Freeze(rb);
        RemoveRecord(rb);
        placed.Add(new PlacedItem { Body = rb, First = first, Units = units });

        PlacedChanged?.Invoke(this);
    }

    /// <summary>清掉这件快递的旧记录，顺手把已被销毁的快递从账本里去掉</summary>
    private void RemoveRecord(Rigidbody rb)
    {
        for (int i = placed.Count - 1; i >= 0; i--)
            if (placed[i].Body == null || placed[i].Body == rb) placed.RemoveAt(i);
    }

    private static void Freeze(Rigidbody rb)
    {
        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    // ---------- 测量（全部在世界空间，不受父级缩放影响） ----------
    /// <summary>这件快递摆到货架上之后，会占掉多宽（沿货架长轴，米）</summary>
    private float LengthWhenPlaced(Rigidbody rb)
    {
        if (rb == null) return 0f;

        // 落位时可能会被摆成货架朝向，先按那个朝向量，才和最终占地一致
        Quaternion target = AlignToRackRotation ? transform.rotation : rb.transform.rotation;
        Quaternion saved = rb.transform.rotation;

        rb.transform.rotation = target;
        float len = MeasureAlong(rb, transform.right);
        rb.transform.rotation = saved;

        return len;
    }

    /// <summary>快递沿某个世界方向的外接长度</summary>
    private static float MeasureAlong(Rigidbody rb, Vector3 worldDir)
    {
        if (rb == null) return 0f;
        if (!TryGetWorldBounds(rb, out Bounds b)) return 0f;

        return ProjectLength(b, worldDir);
    }

    /// <summary>世界 AABB 沿某个方向的投影长度</summary>
    private static float ProjectLength(Bounds b, Vector3 worldDir)
    {
        Vector3 dir = worldDir.normalized;
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
        int cap = Capacity;
        Vector3 axis = transform.right;
        Vector3 up = Vector3.up * 0.05f;

        // 整块货板的 Capacity 格
        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        Gizmos.DrawLine(BoardCenter + axis * EdgeOffset(0), BoardCenter + axis * EdgeOffset(cap));

        for (int i = 0; i <= cap; i++)
        {
            Vector3 p = BoardCenter + axis * EdgeOffset(i);
            Gizmos.DrawLine(p, p + up);
        }

        // 已经占用的格
        Gizmos.color = new Color(1f, 0.75f, 0.2f, 0.95f);
        for (int i = 0; i < placed.Count; i++)
        {
            PlacedItem it = placed[i];
            if (it.Body == null) continue;

            Gizmos.DrawLine(BoardCenter + axis * EdgeOffset(it.First),
                            BoardCenter + axis * EdgeOffset(it.First + it.Units));
        }
    }
}
