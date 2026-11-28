using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 叠人（把别人放到自己头上）：
/// 1. 站在另一个玩家的检测范围内，按 Climb（键盘 E / 手柄 Y）：
///    自己没拿快递、双方都没在叠人时，把对方放到自己头顶；
/// 2. 叠好后，上方玩家的位置完全由下方玩家决定（像被背着走），
///    但上方玩家的抓取 / 投掷照常可用；
///    下方玩家只能移动，抓取和货架取件都被锁住；
/// 3. 下方玩家通过 Throw（投掷键）把头上的人按投掷力度扔出去，双方功能恢复；
/// 4. 不再有「再按一次 Climb 回到身边」的逻辑。
/// </summary>
[RequireComponent(typeof(GrabController))]
[RequireComponent(typeof(ThrowController))]
public class ClimbController : MonoBehaviour
{
    [Header("范围检测")]
    [Tooltip("可选：指定一个 BoxCollider，按它的世界包围盒检测（留空则用下面的半径）")]
    public BoxCollider DetectBox;

    [Tooltip("没有 DetectBox 时使用的检测半径")]
    public float DetectRadius = 1.2f;

    [Header("站位")]
    [Tooltip("站在别人头顶时留出的缝隙")]
    public float MountGap = 0.05f;

    private GrabController grab;
    private ThrowController throwCtl;
    private Rigidbody body;

    private ClimbController rider;    // 我头上的人（我是下方）
    private ClimbController carrier;  // 我踩着的人（我是上方）

    // 叠上去时量一次，之后每帧复用：上方玩家轴心→脚底、下方玩家轴心→头顶
    private float footOffset;
    private float carrierHeadOffset;

    /// <summary>我头上是否背着人（我是下方）</summary>
    public bool IsCarrying => rider != null;

    /// <summary>我是否正被背着（我是上方）</summary>
    public bool IsRiding => carrier != null;

    void Awake()
    {
        grab = GetComponent<GrabController>();
        throwCtl = GetComponent<ThrowController>();
        body = GetComponent<Rigidbody>();

        // 没手动指定时，自动找一个触发用的 BoxCollider 当检测盒
        if (DetectBox == null)
        {
            foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.isTrigger) { DetectBox = box; break; }
            }
        }
    }

    /// <summary>由 PlayerInput 的 Climb 事件调用（键盘 E / 手柄 Y）</summary>
    public void OnClimb(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;   // 只认按下那一刻

        // 已经叠好人时，不再通过 Climb 放下；投掷由 Throw 负责
        if (rider != null || carrier != null) return;

        TryMountRider();
    }

    private void TryMountRider()
    {
        // 我要当底座，自己不能拿着快递
        if (grab.IsHolding) return;

        ClimbController top = FindRiderInRange();
        if (top == null) return;   // 范围内没有可以放到头上的人

        MountRider(top);
    }

    /// <summary>在检测范围里找最近的、可以放到自己头上的玩家</summary>
    private ClimbController FindRiderInRange()
    {
        Collider[] hits = DetectBox != null
            ? Physics.OverlapBox(DetectBox.bounds.center, DetectBox.bounds.extents,
                                 Quaternion.identity, ~0, QueryTriggerInteraction.Collide)
            : Physics.OverlapSphere(transform.position, DetectRadius, ~0, QueryTriggerInteraction.Collide);

        ClimbController best = null;
        float bestSqr = float.MaxValue;

        foreach (Collider hit in hits)
        {
            ClimbController other = hit.GetComponentInParent<ClimbController>();
            if (other == null || other == this) continue;

            // 已经有人在叠他、或者他自己正踩着别人，都不参与
            if (other.rider != null || other.carrier != null) continue;

            // 对方是要放到我头上的人，可以拿着快递；底座不能拿快递在 TryMountRider 里检查

            float sqr = (other.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = other; }
        }

        return best;
    }

    /// <summary>把 top 放到自己的头顶上，自己成为底座</summary>
    private void MountRider(ClimbController top)
    {
        rider = top;
        top.carrier = this;

        // 尺寸要在改物理状态之前量；两个偏移都存到上方玩家身上，SnapToCarrier 用它
        top.footOffset = PivotToBottom(top);
        top.carrierHeadOffset = HeadAbovePivot(this);

        // 下方玩家只能移动：抓取、货架取件全部锁住
        grab.InputLocked = true;
        throwCtl.InputLocked = true;

        // 上方玩家交给「背负」：不再受重力，位置每帧由下方玩家决定
        if (top.body != null)
        {
            top.body.velocity = Vector3.zero;
            top.body.angularVelocity = Vector3.zero;
            top.body.isKinematic = true;
        }

        top.SnapToCarrier();
    }

    /// <summary>
    /// 由 ThrowController 调用：把头上的玩家按投掷力度扔出去，双方输入 / 物理恢复。
    /// </summary>
    public void ThrowRider(Vector3 velocity, Vector3 angularVelocity)
    {
        ClimbController top = rider;
        if (top == null) return;

        rider = null;
        top.carrier = null;

        top.RestorePhysics();

        if (top.body != null)
        {
            top.body.velocity = velocity;
            top.body.angularVelocity = angularVelocity;
        }

        // 下方玩家恢复抓取 / 取件
        grab.InputLocked = false;
        throwCtl.InputLocked = false;
    }

    // 上方玩家跟着下方走：放在 LateUpdate，晚于移动，画面才不抖
    void LateUpdate()
    {
        if (carrier == null) return;

        SnapToCarrier();
        grab.RefreshHold();   // 手上快递一起对齐，免得慢一帧
    }

    /// <summary>吸附到下方玩家头顶</summary>
    private void SnapToCarrier()
    {
        if (carrier == null) return;

        Transform c = carrier.transform;
        Vector3 pos = c.position;
        pos.y = c.position.y + carrierHeadOffset + MountGap + footOffset;

        transform.SetPositionAndRotation(pos, c.rotation);
    }

    private void RestorePhysics()
    {
        if (body == null) return;

        body.isKinematic = false;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    /// <summary>轴心到脚底的距离（取所有实体碰撞体包围盒的下沿）</summary>
    private static float PivotToBottom(ClimbController who)
    {
        float bottom = who.transform.position.y;
        bool has = false;

        foreach (Collider c in who.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger || !c.enabled) continue;
            bottom = has ? Mathf.Min(bottom, c.bounds.min.y) : c.bounds.min.y;
            has = true;
        }

        return Mathf.Max(0f, who.transform.position.y - bottom);
    }

    /// <summary>轴心到头顶的距离（取所有实体碰撞体包围盒的上沿）</summary>
    private static float HeadAbovePivot(ClimbController who)
    {
        float top = who.transform.position.y;
        bool has = false;

        foreach (Collider c in who.GetComponentsInChildren<Collider>())
        {
            if (c.isTrigger || !c.enabled) continue;
            top = has ? Mathf.Max(top, c.bounds.max.y) : c.bounds.max.y;
            has = true;
        }

        return Mathf.Max(0f, top - who.transform.position.y);
    }

    // 兜底：自己被禁用 / 销毁时解开叠人关系，免得对方输入被永久锁住
    void OnDisable()
    {
        if (carrier != null)
        {
            carrier.rider = null;
            carrier.grab.InputLocked = false;
            carrier.throwCtl.InputLocked = false;
            carrier = null;
        }

        if (rider != null)
        {
            rider.carrier = null;
            rider.RestorePhysics();
            rider = null;

            grab.InputLocked = false;
            throwCtl.InputLocked = false;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.35f);

        if (DetectBox != null)
            Gizmos.DrawWireCube(DetectBox.bounds.center, DetectBox.bounds.size);
        else
            Gizmos.DrawWireSphere(transform.position, DetectRadius);
    }
}
