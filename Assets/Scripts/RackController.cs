using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 货架：挂在 Tag="rack" 的物体上（该物体需要一个 BoxCollider 作为触发区）。
/// ------------------------------------------------------------------
/// 用「槽位」来摆放：Slots 里放 6 个空物体作为偏移位置（-X → +X）。
/// 快递占几格由它自身的 X 尺寸决定：占位 = Ceil(X尺寸 / SlotSpacing)。
///   按 X 尺寸 0.2 / 0.4 / 0.6 时，小=1 格、中=2 格、大=3 格。
/// 摆放从第 0 个槽位开始往 +X 依次占据；
/// 实际位置 = (第一个占用槽位 + 最后一个占用槽位) / 2。
/// 从 +X 端往 -X 端依次取下（按 Q，见 ThrowController）。
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class RackController : MonoBehaviour
{
    [Header("槽位（按 -X → +X 顺序）")]
    [Tooltip("拖入 6 个子物体作为偏移位置；Slots[0] 是最左边（-X）起点")]
    public Transform[] Slots;

    [Tooltip("相邻槽位的间隔（米），同时用于把快递 X 尺寸换算成占位格数")]
    [Range(0.05f, 0.5f)]
    public float SlotSpacing = 0.2f;

    [Tooltip("勾选后按 SlotSpacing 自动把 6 个槽位等距排开（以 Slots[0] 为起点，只改本地 X）")]
    public bool AutoLayoutSlots = true;

    [Header("摆放朝向")]
    [Tooltip("勾选：快递跟随货架旋转；不勾：保持快递被放下时的朝向")]
    public bool AlignToRackRotation = true;

    // 接下来可用的第一个槽位下标（0 在 -X 端，依次往 +X 累加）
    private int usedSlots;

    // 已摆放的快递，按放置顺序记录，方便从末尾（+X 端）按 Q 取下
    private class PlacedItem
    {
        public Rigidbody Body;
        public int SlotCount;
    }
    private readonly List<PlacedItem> placed = new List<PlacedItem>();

    public int SlotCount => Slots != null && Slots.Length > 0 ? Slots.Length : 6;
    public int UsedSlots => usedSlots;
    public int RemainingSlots => Mathf.Max(0, SlotCount - usedSlots);
    public int PlacedCount => placed.Count;

    void Awake()
    {
        GetComponent<BoxCollider>().isTrigger = true;
        LayoutSlots();
    }

    // 编辑器里拖动 SlotSpacing 滑块时实时重排槽位
    void OnValidate()
    {
        LayoutSlots();
    }

    private void LayoutSlots()
    {
        if (!AutoLayoutSlots || Slots == null || Slots.Length == 0 || Slots[0] == null) return;

        Vector3 origin = Slots[0].localPosition;
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] == null) continue;

            Vector3 p = origin;
            p.x = origin.x + i * SlotSpacing;   // 沿货架本地 X 等距
            Slots[i].localPosition = p;
        }
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
    /// <summary>这件快递占几个槽位（由它自身的 X 尺寸决定）</summary>
    public int SlotsFor(Rigidbody rb)
    {
        if (rb == null) return 0;

        float xSize = MeasureX(rb);
        // 减去一个极小量，避免 0.4/0.2 的浮点误差被 Ceil 成 3
        int need = Mathf.CeilToInt(xSize / Mathf.Max(0.05f, SlotSpacing) - 0.0001f);
        return Mathf.Max(1, need);   // 不封顶：超过 SlotCount 时 CanPlace 自然为 false
    }

    /// <summary>是否放得下：长度不超标 且 从 -X 往 +X 连续占用不越界</summary>
    public bool CanPlace(Rigidbody rb)
    {
        int need = SlotsFor(rb);
        return need > 0 && usedSlots + need <= SlotCount;
    }

    // ---------- 放 / 取 ----------
    /// <summary>把一件快递摆到货架上，成功返回 true</summary>
    public bool Place(Rigidbody rb)
    {
        if (rb == null || !CanPlace(rb)) return false;

        int need = SlotsFor(rb);
        int first = usedSlots;          // 从当前末尾继续往 +X
        int last = first + need - 1;

        // 实际摆放位置 = (第一个占用槽位 + 最后一个占用槽位) / 2
        Vector3 center = (SlotWorld(first) + SlotWorld(last)) * 0.5f;
        // 抬高半个快递高度，让底面贴住槽位所在平面
        center += transform.up * MeasureHalfHeight(rb);

        Quaternion rot = AlignToRackRotation ? transform.rotation : rb.transform.rotation;

        // 先定好世界变换，再挂到货架下（SetParent 保持世界变换），
        // 父空物体的位移/旋转/缩放都不会造成偏移
        rb.transform.SetPositionAndRotation(center, rot);
        rb.transform.SetParent(transform, true);

        // 触发区撑不住动态刚体，固定住；同时跟随货架移动
        rb.isKinematic = true;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        usedSlots += need;
        placed.Add(new PlacedItem { Body = rb, SlotCount = need });
        return true;
    }

    /// <summary>从 +X 端取下一件（后放的先取）。货架为空返回 null</summary>
    public Rigidbody TakeLast()
    {
        if (placed.Count == 0) return null;

        PlacedItem item = placed[placed.Count - 1];
        placed.RemoveAt(placed.Count - 1);
        usedSlots -= item.SlotCount;   // 归还槽位，下次放置会补回同位置

        item.Body.transform.SetParent(null, true);
        item.Body.isKinematic = false;
        item.Body.velocity = Vector3.zero;
        item.Body.angularVelocity = Vector3.zero;
        return item.Body;
    }

    private Vector3 SlotWorld(int index)
    {
        if (Slots != null && index >= 0 && index < Slots.Length && Slots[index] != null)
            return Slots[index].position;

        // 没配槽位时的兜底：以货架本地 X 等距计算
        float x = (index - (SlotCount - 1) * 0.5f) * SlotSpacing;
        return transform.TransformPoint(new Vector3(x, 0f, 0f));
    }

    /// <summary>快递沿摆放方向的长度（世界单位）</summary>
    private static float MeasureX(Rigidbody rb)
    {
        BoxCollider box = rb.GetComponentInChildren<BoxCollider>();
        if (box != null)
            return box.size.x * Mathf.Abs(box.transform.lossyScale.x);

        return Mathf.Abs(rb.transform.lossyScale.x);
    }

    /// <summary>快递的半高（世界单位）</summary>
    private static float MeasureHalfHeight(Rigidbody rb)
    {
        BoxCollider box = rb.GetComponentInChildren<BoxCollider>();
        if (box != null)
            return box.size.y * Mathf.Abs(box.transform.lossyScale.y) * 0.5f;

        float h = 0f;
        foreach (Renderer r in rb.GetComponentsInChildren<Renderer>())
            h = Mathf.Max(h, r.bounds.size.y);
        return (h > 0.01f ? h : 0.2f) * 0.5f;
    }

    void OnDrawGizmosSelected()
    {
        if (Slots == null) return;

        Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Slots[i] == null) continue;
            Gizmos.DrawWireSphere(Slots[i].position, 0.03f);
        }
    }
}
