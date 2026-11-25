using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 交付（Give）：把手里的快递放到名为 Give 的桌子正中央。
/// 触发方式和投掷完全一样 —— 绑在 PlayerInput 的 Throw 动作上（键盘左键 / 手柄扳机）。
/// 站在桌子旁边按下时走「放下」，不在桌边时仍然由 ThrowController 正常蓄力投掷。
///
/// 规则：
///   1. 只能放、不能拿 —— 放下后去掉 CanGrab 标签并设为 kinematic；
///   2. 桌上只能有一件 —— 已经放了就拒绝；
///   3. 玩家必须站在桌子旁边。
/// </summary>
[DisallowMultipleComponent]
public class GiveControl : MonoBehaviour
{
    [Header("桌子")]
    [Tooltip("目标桌子；留空时自动在场景里查找名为 Give 的物体")]
    public Transform Table;

    [Tooltip("玩家离桌面边缘多近才能交付（米，只算水平距离）")]
    public float GiveRange = 1.2f;

    [Header("顾客 / 判定")]
    [Tooltip("交付目标顾客；留空时自动找场景里的 CustomerOrder")]
    public CustomerOrder Customer;

    [Tooltip("快递摆到桌上后多久消失并刷新顾客需求（秒），0 = 立即")]
    [Min(0f)] public float ConsumeDelay = 0.35f;

    /// <summary>交付结果：参数为「是否正确」和「当时顾客的订单」</summary>
    public event System.Action<bool, ParcelOrder> Delivered;

    [Header("运行时状态")]
    [Tooltip("当前放在桌上的那件快递（只读）")]
    [System.NonSerialized] public Rigidbody PlacedBody;

    private const string GrabTag = "CanGrab";

    private GrabController hands;
    private Collider tableCollider;
    private CustomerPatience patience;

    /// <summary>桌上是不是已经有快递了</summary>
    public bool HasPlaced => PlacedBody != null;

    void Awake()
    {
        hands = GetComponent<GrabController>();
        ResolveTable();
    }

    /// <summary>
    /// 由 PlayerInput 的 Throw 动作调用（和 ThrowController.OnThrow 绑同一个事件）。
    /// 在桌边、手里有货、桌上还空着时，按下就把货放到桌子正中央。
    /// </summary>
    public void OnGive(InputAction.CallbackContext ctx)
    {
        // 按钮会依次触发 started / performed / canceled，只认按下那一刻
        if (!ctx.performed) return;
        TryGive();
    }

    /// <summary>尝试交付，成功返回 true</summary>
    public bool TryGive()
    {
        if (hands == null) hands = GetComponent<GrabController>();
        if (hands == null) return false;

        ResolveTable();
        if (Table == null) return false;

        if (PlacedBody != null) return false;   // 桌上已经有一件（被销毁时会自动变 null）
        if (!hands.IsHolding) return false;     // 手里没东西可交
        if (!InRange()) return false;           // 离桌子太远

        Rigidbody rb = hands.Release();         // 取出叠在最上层的那件
        if (rb == null) return false;

        PlaceOnTable(rb);
        Deliver(rb);      // 判定对错 → 稍后消失 → 顾客刷新需求
        return true;
    }

    /// <summary>把桌上那件快递恢复成可抓取状态（调试 / 重开一局时用）</summary>
    public void ClearTable()
    {
        if (PlacedBody == null) return;

        Rigidbody rb = PlacedBody;
        PlacedBody = null;
        SetGrabbable(rb, true);
    }

    // ---------- 放置 ----------

    private void PlaceOnTable(Rigidbody rb)
    {
        Quaternion rot = Table.rotation;

        // 先摆正再量高度，量出来的才是放上去以后的实际高度
        rb.transform.rotation = rot;
        float height = MeasureHeight(rb);

        Vector3 center;
        if (TryGetTableBounds(out Bounds b))
        {
            // 水平取桌面中心，竖直让底面贴着桌面
            center = new Vector3(b.center.x, b.max.y + height * 0.5f + 0.005f, b.center.z);
        }
        else
        {
            center = Table.position + Vector3.up * height * 0.5f;
        }

        rb.transform.SetPositionAndRotation(center, rot);

        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        SetGrabbable(rb, false);   // 从这一刻起谁都拿不起来
        PlacedBody = rb;
    }

    // ---------- 交付判定 ----------

    /// <summary>判断这件快递对不对，并安排它消失 + 刷新顾客需求</summary>
    private void Deliver(Rigidbody rb)
    {
        if (Customer == null) Customer = FindObjectOfType<CustomerOrder>();

        if (Customer == null)
        {
            Debug.LogWarning("[Give] 场景里没有 CustomerOrder，交付判定跳过", this);
            StartCoroutine(ConsumeRoutine(rb, -1, false));
            return;
        }

        ParcelOrder order = Customer.CurrentOrder;
        int orderVersion = Customer.Version;
        bool correct = IsMatch(rb, order);

        Delivered?.Invoke(correct, order);

        // 通知顾客的耐心条：错误降一格（正确默认不变）
        if (patience == null) patience = Customer.GetComponent<CustomerPatience>();
        if (patience != null) patience.NotifyDelivered(correct);

        Debug.Log(correct
            ? $"[Give] 交付正确：{Describe(rb)}"
            : $"[Give] 交付错误：顾客要 {order.DisplayText}，实际 {Describe(rb)}", this);

        StartCoroutine(ConsumeRoutine(rb, orderVersion, correct));
    }

    /// <summary>
    /// 不管对错，桌上的快递都会消失；
    /// 只有交付正确才换新需求，送错只是扣耐心（由 CustomerPatience 处理），顾客继续等。
    /// 耐心耗尽后的换需求由 CustomerPatience.OnDepleted 负责。
    /// </summary>
    private IEnumerator ConsumeRoutine(Rigidbody rb, int orderVersion, bool correct)
    {
        if (ConsumeDelay > 0f) yield return new WaitForSeconds(ConsumeDelay);
        else yield return null;

        if (rb != null) Destroy(rb.gameObject);
        PlacedBody = null;

        // 送错不换需求；等待期间如果顾客已经因为耐心耗尽换过需求，也不要重复刷新
        if (correct && Customer != null && Customer.Version == orderVersion)
            Customer.RollNewOrder();
    }

    /// <summary>尺寸和颜色是否都符合顾客需求</summary>
    private static bool IsMatch(Rigidbody rb, ParcelOrder order)
    {
        GrabbableItem item = rb.GetComponentInParent<GrabbableItem>();
        if (item == null) return false;

        return order.Size == ToOrderSize(item.Size)
            && order.ColorMask == ParcelColor.GetColorMask(rb);
    }

    /// <summary>把快递转成显示文字，例如 "Large Red+Blue"</summary>
    private static string Describe(Rigidbody rb)
    {
        GrabbableItem item = rb.GetComponentInParent<GrabbableItem>();
        OrderSize size = item != null ? ToOrderSize(item.Size) : OrderSize.Small;

        return new ParcelOrder(size, ParcelColor.GetColorMask(rb)).DisplayText;
    }

    private static OrderSize ToOrderSize(GrabbableItem.ItemSize size)
    {
        if (size == GrabbableItem.ItemSize.Medium) return OrderSize.Medium;
        if (size == GrabbableItem.ItemSize.Large) return OrderSize.Large;
        return OrderSize.Small;
    }

    /// <summary>只保留「可抓」这一种状态切换：kinematic + 去掉 CanGrab 标签</summary>
    private static void SetGrabbable(Rigidbody rb, bool grabbable)
    {
        rb.isKinematic = !grabbable;
        rb.tag = grabbable ? GrabTag : "Untagged";

        foreach (Collider c in rb.GetComponentsInChildren<Collider>(true))
            c.tag = grabbable ? GrabTag : "Untagged";
    }

    private static float MeasureHeight(Rigidbody rb)
    {
        float h = 0f;
        foreach (Renderer r in rb.GetComponentsInChildren<Renderer>())
            h = Mathf.Max(h, r.bounds.size.y);

        return h > 0.01f ? h : 0.3f;
    }

    // ---------- 范围 / 桌面信息 ----------

    private void ResolveTable()
    {
        if (Table != null) return;

        GameObject go = GameObject.Find("Give");
        if (go != null) Table = go.transform;
    }

    /// <summary>玩家是否站在桌子旁边（只比较水平距离）</summary>
    private bool InRange()
    {
        if (!TryGetTableBounds(out Bounds b)) return false;

        Vector3 p = transform.position;
        float dx = Mathf.Max(0f, Mathf.Abs(p.x - b.center.x) - b.extents.x);
        float dz = Mathf.Max(0f, Mathf.Abs(p.z - b.center.z) - b.extents.z);

        return dx * dx + dz * dz <= GiveRange * GiveRange;
    }

    private bool TryGetTableBounds(out Bounds bounds)
    {
        bounds = default;
        if (Table == null) return false;

        if (tableCollider == null)
            tableCollider = Table.GetComponentInChildren<Collider>();

        if (tableCollider != null)
        {
            bounds = tableCollider.bounds;
            return true;
        }

        Renderer[] renderers = Table.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return true;
        }

        bounds = new Bounds(Table.position, Vector3.one * 0.2f);
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        Transform table = Table;
        if (table == null)
        {
            GameObject go = GameObject.Find("Give");
            if (go != null) table = go.transform;
        }
        if (table == null) return;

        Collider col = table.GetComponentInChildren<Collider>();
        Bounds b = col != null ? col.bounds : new Bounds(table.position, Vector3.one * 0.2f);

        // 可交付范围
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(
            b.center,
            new Vector3(b.size.x + GiveRange * 2f, 0.02f, b.size.z + GiveRange * 2f));

        // 快递落点（桌面正中央）
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(new Vector3(b.center.x, b.max.y + 0.005f, b.center.z), 0.06f);
    }
}
