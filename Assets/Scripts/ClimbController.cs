using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 叠人（把人背在头上）：
/// 1. 站在另一个玩家的检测范围内，按 Climb（键盘 E / 手柄 Y）：
///    对方没拿快递、且两人都没在叠人时，自己瞬移到对方正上方；
/// 2. 叠好后，上方玩家的位置完全由下方玩家决定（像被背着走），
///    但上方玩家的抓取 / 投掷照常可用；
///    下方玩家只能移动，抓取和货架取件都被锁住；
/// 3. 上方玩家再按一次 Climb：瞬移到下方玩家水平方向身边，双方功能全部恢复；
/// 4. 下方玩家在有人背着时按 Climb：把上面的人放到身边，同样全部恢复。
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

    [Tooltip("下车时停在对方身边多远处")]
    public float DismountOffset = 0.8f;

    private GrabController grab;
    private ThrowController throwCtl;
    private Rigidbody body;

    private ClimbController rider;    // 我背着的人（我是下方）
    private ClimbController carrier;  // 我踩着的人（我是上方）

    // 上车时量一次，之后每帧复用：自己轴心→脚底、下方玩家轴心→头顶
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

        if (rider != null) { DismountRider(); return; }      // 我是下方：把上面的人放下来
        if (carrier != null) { carrier.DismountRider(); return; } // 我是上方：自己下车

        TryClimb();
    }

    private void TryClimb()
    {
        ClimbController bottom = FindBottomInRange();
        if (bottom == null) return;   // 范围内没有可以踩的人

        MountOn(bottom);
    }

    /// <summary>在检测范围里找最近的、可以踩上去的玩家</summary>
    private ClimbController FindBottomInRange()
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

            // 站在地面的那个人手里不能有快递（头上的人可以有）
            if (other.grab.IsHolding) continue;

            float sqr = (other.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr) { bestSqr = sqr; best = other; }
        }

        return best;
    }

    /// <summary>爬到 bottom 的头顶上</summary>
    private void MountOn(ClimbController bottom)
    {
        carrier = bottom;
        bottom.rider = this;

        // 尺寸要在改物理状态之前量
        footOffset = PivotToBottom(this);
        carrierHeadOffset = HeadAbovePivot(bottom);

        // 下方玩家只能移动：抓取、货架取件全部锁住
        bottom.grab.InputLocked = true;
        bottom.throwCtl.InputLocked = true;

        // 上方玩家交给"背负"：不再受重力，位置每帧由下方玩家决定
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }

        SnapToCarrier();
    }

    /// <summary>把背上的人放到自己身边，双方输入 / 物理全部恢复（我是下方）</summary>
    private void DismountRider()
    {
        ClimbController top = rider;
        if (top == null) return;

        rider = null;
        top.carrier = null;

        top.SnapBeside(this);
        top.RestorePhysics();

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

    /// <summary>挪到下方玩家水平方向身边</summary>
    private void SnapBeside(ClimbController bottom)
    {
        Transform b = bottom.transform;
        Vector3 pos = b.position + b.right * DismountOffset;
        transform.SetPositionAndRotation(pos, b.rotation);
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
