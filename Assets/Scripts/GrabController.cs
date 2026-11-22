using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 拿起 / 放下身前的立方体。
/// 判定：只有打了 CanGrab 标签、且不是玩家的物体才能抓取。
/// </summary>
public class GrabController : MonoBehaviour
{
    [Header("抓取")]
    [Tooltip("以玩家为中心搜索可抓取物的半径")]
    public float GrabRange = 1.3f;

    [Tooltip("举起后物体相对玩家的位置（世界单位）；+Z 是玩家前方")]
    public Vector3 HoldOffset = new Vector3(0f, 0.2f, 0.85f);

    [Tooltip("超过这个质量的物体搬不动")]
    public float MaxMass = 20f;

    // 可抓取物体的标签（在 Tag Manager 里定义）
    private const string GrabTag = "CanGrab";

    // 全局记录正在被举起的物体，避免两个玩家抢同一个
    private static readonly HashSet<Rigidbody> HeldBodies = new HashSet<Rigidbody>();

    private Rigidbody held;
    private Rigidbody selfBody;

    public bool IsHolding => held != null;

    // 举起后物体应该在的位置 / 朝向（世界空间，不受玩家缩放影响）
    public Vector3 HoldPosition => transform.position + transform.rotation * HoldOffset;
    private Quaternion HoldRotation => transform.rotation;

    void Awake()
    {
        selfBody = GetComponent<Rigidbody>();
    }

    /// <summary>由 PlayerInput 的 Grab 事件调用</summary>
    public void OnGrab(InputAction.CallbackContext ctx)
    {
        // 按钮动作会依次触发 started / performed / canceled，只认按下那一刻
        if (!ctx.performed) return;

        if (IsHolding) Drop();
        else TryGrab();
    }

    private void TryGrab()
    {
        Collider[] hits = Physics.OverlapSphere(
            transform.position, GrabRange, ~0, QueryTriggerInteraction.Ignore);

        Rigidbody best = null;
        float bestSqr = float.MaxValue;

        foreach (Collider col in hits)
        {
            Rigidbody rb = col.attachedRigidbody;

            // 同一个物体可能命中多个碰撞体，跳过不可抓的和已经更近的候选
            if (!CanGrab(rb, col)) continue;

            float sqr = (rb.worldCenterOfMass - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = rb;
            }
        }

        if (best != null) Grab(best);
    }

    private bool CanGrab(Rigidbody rb, Collider col)
    {
        if (rb == null) return false;                       // 静态场景物体（地面、台面）
        if (!rb.CompareTag(GrabTag) && !col.CompareTag(GrabTag)) return false; // 没打标签的不能抓
        if (rb == selfBody) return false;                   // 自己
        if (rb.isKinematic) return false;                   // 被设为 kinematic 的物体
        if (rb.mass > MaxMass) return false;                // 太重
        if (HeldBodies.Contains(rb)) return false;          // 已被别人拿着
        if (rb.GetComponentInParent<GrabController>() != null) return false; // 其他玩家
        return true;
    }

    private void Grab(Rigidbody rb)
    {
        held = rb;
        HeldBodies.Add(rb);

        // 举着期间不参与物理：不关碰撞体的话，方块会把玩家顶开
        SetColliders(rb, false);
        rb.isKinematic = true;

        rb.transform.SetPositionAndRotation(HoldPosition, HoldRotation);
    }

    private void Drop()
    {
        Rigidbody rb = held;
        held = null;
        HeldBodies.Remove(rb);

        // 先摆到玩家面前，再交还给物理（重力负责让它自由落下）
        rb.transform.SetPositionAndRotation(HoldPosition, HoldRotation);
        rb.position = HoldPosition;
        rb.rotation = HoldRotation;

        SetColliders(rb, true);
        rb.isKinematic = false;
        rb.velocity = Vector3.zero;          // 否则切换瞬间会弹飞
        rb.angularVelocity = Vector3.zero;
    }

    // 玩家在 Update 里移动，放在 LateUpdate 同步，方块才不会抖
    private void LateUpdate()
    {
        if (held == null) return;
        held.transform.SetPositionAndRotation(HoldPosition, HoldRotation);
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

        // 举起位置
        Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
        Gizmos.DrawWireCube(HoldPosition, Vector3.one * 0.5f);
        Gizmos.DrawLine(transform.position, HoldPosition);
    }
}
