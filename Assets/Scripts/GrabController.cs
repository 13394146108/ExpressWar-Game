using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 拿起 / 叠放持有物（投掷见 ThrowController）。
/// 按重量装填：小=1 中=2 大=3，玩家总承重默认 3。
/// 第一个物体放在 HoldOffset，之后依次往上叠；投掷时从最上层取走。
/// </summary>
public class GrabController : MonoBehaviour
{
    [Header("抓取")]
    [Tooltip("以玩家为中心搜索可抓取物的半径")]
    public float GrabRange = 1.3f;

    //[Tooltip("超过这个物理质量的物体搬不动（和承重无关）")]
    //public float MaxMass = 20f;

    [Tooltip("玩家能拿起的总重量")]
    public int MaxCarryWeight = 3;

    [Tooltip("最多能叠几个物体")]
    public int MaxSlots = 3;

    [Header("持有位置")]
    [Tooltip("第一个物体的位置（世界单位）；+Z 是玩家前方")]
    public Vector3 HoldOffset = new Vector3(0f, 0.2f, 0.85f);

    [Tooltip("相邻两个物体之间的空隙")]
    public float SlotGap = 0.04f;

    // 可抓取物体的标签（在 Tag Manager 里定义）
    private const string GrabTag = "CanGrab";

    // 全局记录正在被举起的物体，避免两个玩家抢同一个
    private static readonly HashSet<Rigidbody> HeldBodies = new HashSet<Rigidbody>();

    /// <summary>为 true 时忽略抓取输入（背人期间由 ClimbController 关掉）</summary>
    [System.NonSerialized] public bool InputLocked;

    /// <summary>手里拿着的一个物体（列表按从下到上排列）</summary>
    private class HeldItem
    {
        public Rigidbody Body;
        public int Weight;
        public float Height;   // 世界单位高度，用来算上一个槽位
    }

    private readonly List<HeldItem> stack = new List<HeldItem>();
    private Rigidbody selfBody;

    public bool IsHolding => stack.Count > 0;
    public int HeldCount => stack.Count;

    /// <summary>最上层物体（货架放置时用来判断尺寸），手里没东西时为 null</summary>
    public Rigidbody TopBody => stack.Count > 0 ? stack[stack.Count - 1].Body : null;

    /// <summary>当前总重量</summary>
    public int CurrentWeight
    {
        get
        {
            int w = 0;
            for (int i = 0; i < stack.Count; i++) w += stack[i].Weight;
            return w;
        }
    }

    /// <summary>还能再拿多少重量</summary>
    public int RemainingWeight => Mathf.Max(0, MaxCarryWeight - CurrentWeight);

    /// <summary>最上层物体所在的位置（投掷起点 / Gizmo 用）</summary>
    public Vector3 HoldPosition => SlotPosition(Mathf.Max(0, stack.Count - 1));

    private Vector3 BaseHoldPosition => transform.position + transform.rotation * HoldOffset;
    private Quaternion HoldRotation => transform.rotation;

    void Awake()
    {
        selfBody = GetComponent<Rigidbody>();
    }

    /// <summary>由 PlayerInput 的 Grab 事件调用：只负责拿，不再负责放</summary>
    public void OnGrab(InputAction.CallbackContext ctx)
    {
        // 按钮动作会依次触发 started / performed / canceled，只认按下那一刻
        if (!ctx.performed) return;
        if (InputLocked) return;   // 背人期间抓取被锁

        TryGrab();
    }

    private void TryGrab()
    {
        if (stack.Count >= MaxSlots) return;   // 槽位已经叠满

        int remaining = RemainingWeight;
        if (remaining <= 0) return;            // 背不动了

        Collider[] hits = Physics.OverlapSphere(
            transform.position, GrabRange, ~0, QueryTriggerInteraction.Ignore);

        HeldItem best = null;
        float bestSqr = float.MaxValue;

        foreach (Collider col in hits)
        {
            Rigidbody rb = col.attachedRigidbody;

            // 同一个物体可能命中多个碰撞体，跳过不可抓的和已经更近的候选
            if (!CanGrab(rb, col)) continue;

            GrabbableItem item = rb.GetComponentInParent<GrabbableItem>();
            int weight = item != null ? item.Weight : 1;   // 没挂组件时按"小"处理

            if (weight > remaining) continue;              // 这一个就超重，换下一个

            float sqr = (rb.worldCenterOfMass - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = new HeldItem
                {
                    Body = rb,
                    Weight = weight,
                    Height = MeasureHeight(rb)
                };
            }
        }

        if (best != null) AddToStack(best);
    }

    private bool CanGrab(Rigidbody rb, Collider col)
    {
        if (rb == null) return false;                       // 静态场景物体（地面、台面）
        if (!rb.CompareTag(GrabTag) && !col.CompareTag(GrabTag)) return false; // 没打标签的不能抓
        if (rb == selfBody) return false;                   // 自己
        if (rb.isKinematic) return false;                   // 被设为 kinematic 的物体
        //if (rb.mass > MaxMass) return false;                // 太重
        if (HeldBodies.Contains(rb)) return false;          // 已被别人拿着
        if (rb.GetComponentInParent<GrabController>() != null) return false; // 其他玩家
        return true;
    }

    private void AddToStack(HeldItem item)
    {
        HeldBodies.Add(item.Body);

        // 举起期间不参与物理：不关碰撞体的话，方块会把玩家顶开
        SetColliders(item.Body, false);
        item.Body.isKinematic = true;

        stack.Add(item);              // 新拿到的永远在最上面
        UpdateStackPositions();       // 立刻摆到对应槽位
    }

    /// <summary>
    /// 取出最上层物体并交还给物理，返回那个刚体（没拿东西时返回 null）。
    /// 投掷 / 交给别人都走这里 —— 所以取出的顺序永远是"从上至下"。
    /// </summary>
    public Rigidbody Release()
    {
        if (stack.Count == 0) return null;

        UpdateStackPositions();       // 先确保它就在当前槽位上

        int top = stack.Count - 1;
        HeldItem item = stack[top];
        stack.RemoveAt(top);

        HeldBodies.Remove(item.Body);
        SetColliders(item.Body, true);
        item.Body.isKinematic = false;

        return item.Body;
    }

    /// <summary>
    /// 把货架上取下的一件物体直接放回手上（供 RackController 取件用）。
    /// 装不下 / 已拿着 / 被别人拿着时返回 false，且不改动任何状态。
    /// </summary>
    public bool TryAddToHands(Rigidbody rb)
    {
        if (rb == null) return false;
        if (stack.Count >= MaxSlots) return false;

        GrabbableItem item = rb.GetComponentInParent<GrabbableItem>();
        int weight = item != null ? item.Weight : 1;

        if (weight > RemainingWeight) return false;
        if (HeldBodies.Contains(rb)) return false;

        HeldBodies.Add(rb);

        rb.transform.SetParent(null, true);   // 若还挂在货架下，先脱离
        SetColliders(rb, false);              // 举起期间不参与物理
        rb.isKinematic = true;

        stack.Add(new HeldItem
        {
            Body = rb,
            Weight = weight,
            Height = MeasureHeight(rb)
        });
        UpdateStackPositions();
        return true;
    }

    /// <summary>第 index 个槽位的世界坐标（物体轴心所在处）</summary>
    private Vector3 SlotPosition(int index)
    {
        Vector3 p = BaseHoldPosition;

        for (int i = 0; i < index; i++)
        {
            float h = i < stack.Count ? stack[i].Height : 0.5f;
            float nextH = i + 1 < stack.Count ? stack[i + 1].Height : h;

            // 物体轴心从下往上累加：一半自身高度 + 空隙 + 一半下一个物体高度
            p += Vector3.up * (h * 0.5f + SlotGap + nextH * 0.5f);
        }

        return p;
    }

    /// <summary>立刻把手上物体对齐到当前槽位（被背着时位置突变后补一次，避免慢一帧）</summary>
    public void RefreshHold()
    {
        UpdateStackPositions();
    }

    private void UpdateStackPositions()
    {
        if (stack.Count == 0) return;

        Quaternion rot = HoldRotation;
        for (int i = 0; i < stack.Count; i++)
            stack[i].Body.transform.SetPositionAndRotation(SlotPosition(i), rot);
    }

    // 玩家在 Update 里移动，放在 LateUpdate 同步，叠着的物体才不会抖
    private void LateUpdate()
    {
        UpdateStackPositions();
    }

    /// <summary>量出物体的世界高度，用来决定下一个槽位往上抬多少</summary>
    private static float MeasureHeight(Rigidbody rb)
    {
        float h = 0f;
        foreach (Renderer r in rb.GetComponentsInChildren<Renderer>())
            h = Mathf.Max(h, r.bounds.size.y);

        return h > 0.01f ? h : 0.5f;   // 没有 Renderer 时按 0.5 兜底
    }

    private static void SetColliders(Rigidbody rb, bool enabled)
    {
        foreach (Collider c in rb.GetComponentsInChildren<Collider>())
            c.enabled = enabled;
    }

    private void OnDrawGizmosSelected()
    {
        // 可抓取范围
        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, GrabRange);

        // 三个叠放槽位
        for (int i = 0; i < MaxSlots; i++)
        {
            float h = i < stack.Count ? stack[i].Height : 0.5f;

            Gizmos.color = i < stack.Count
                ? new Color(1f, 0.8f, 0.2f, 0.9f)
                : new Color(1f, 0.8f, 0.2f, 0.25f);

            Gizmos.DrawWireCube(SlotPosition(i), new Vector3(0.5f, h, 0.5f));
        }

        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
        Gizmos.DrawLine(transform.position, BaseHoldPosition);
    }
}
