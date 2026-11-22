using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 蓄力投掷：按住鼠标左键蓄力，松开把手上的"最上层"物体按抛物线投出。
/// 只管力度和弹道，叠放与取物由 GrabController 负责。
/// </summary>
[RequireComponent(typeof(GrabController))]
public class ThrowController : MonoBehaviour
{
    [Header("蓄力")]
    [Tooltip("按住多久达到最大力度（秒）")]
    public float MaxChargeTime = 1.2f;

    [Header("弹道")]
    [Tooltip("轻点一下的最小初速度")]
    public float MinSpeed = 5f;

    [Tooltip("蓄满力的最大初速度")]
    public float MaxSpeed = 13f;

    [Tooltip("发射角（相对水平面），35° 左右抛物线手感最自然")]
    [Range(5f, 80f)]
    public float ThrowAngle = 35f;

    [Tooltip("投出瞬间的自旋速度，0 表示不转")]
    public float SpinSpeed = 8f;

    private GrabController grab;
    private bool charging;
    private float chargeTime;

    public bool IsCharging => charging;

    /// <summary>蓄力进度 0~1，以后做蓄力条 UI 直接读这个</summary>
    public float ChargeRatio => Mathf.Clamp01(chargeTime / Mathf.Max(0.01f, MaxChargeTime));

    void Awake()
    {
        grab = GetComponent<GrabController>();
    }

    void Update()
    {
        // 蓄力中手上的东西没了（理论上不会发生，防御性处理），立刻中断
        if (charging && !grab.IsHolding)
        {
            charging = false;
            chargeTime = 0f;
            return;
        }

        if (!charging) return;
        chargeTime = Mathf.Min(chargeTime + Time.deltaTime, MaxChargeTime);
    }

    /// <summary>由 PlayerInput 的 Throw 事件调用：按下开始蓄力，松开投出</summary>
    public void OnThrow(InputAction.CallbackContext ctx)
    {
        if (ctx.started) BeginCharge();
        else if (ctx.canceled) Throw();
    }

    private void BeginCharge()
    {
        if (!grab.IsHolding) return;   // 手上没东西就不蓄力
        charging = true;
        chargeTime = 0f;
    }

    private void Throw()
    {
        if (!charging) return;

        charging = false;
        float ratio = ChargeRatio;
        chargeTime = 0f;

        Rigidbody rb = grab.Release();   // 从最上层取走，交还给物理
        if (rb == null) return;

        rb.velocity = LaunchDirection() * Mathf.Lerp(MinSpeed, MaxSpeed, ratio);
        rb.angularVelocity = transform.right * (-SpinSpeed * ratio);
    }

    /// <summary>固定发射角，只让速度随蓄力变化 —— 弧线形状稳定，落点可预期</summary>
    private Vector3 LaunchDirection()
    {
        float rad = ThrowAngle * Mathf.Deg2Rad;
        return transform.forward * Mathf.Cos(rad) + Vector3.up * Mathf.Sin(rad);
    }

    private void OnDrawGizmosSelected()
    {
        // 运行中且手上有东西时，画预测抛物线（选中玩家在 Scene 视图可见）
        if (!Application.isPlaying) return;

        if (grab == null) grab = GetComponent<GrabController>();
        if (grab == null || !grab.IsHolding) return;

        Vector3 p = grab.HoldPosition;
        Vector3 v = LaunchDirection() * Mathf.Lerp(MinSpeed, MaxSpeed, ChargeRatio);

        Gizmos.color = Color.Lerp(Color.green, Color.red, ChargeRatio);
        const float dt = 0.06f;

        for (int i = 0; i < 40; i++)
        {
            Vector3 next = p + v * dt + 0.5f * Physics.gravity * dt * dt;
            v += Physics.gravity * dt;
            Gizmos.DrawLine(p, next);
            p = next;
            if (p.y < 0f) break;
        }
    }
}
