using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 蓄力投掷：按住鼠标左键蓄力，松开把手上的"最上层"物体按抛物线投出。
/// 只在货架触发区外投掷 —— 站在货架旁时，同一个键改成「放上去」：
/// 按货架长轴从负向到正向依次落位（见 RackController）；拿取仍取离站位最近的那件。
/// 叠放与取物本身由 GrabController 负责。
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
    private RackController currentRack;

    /// <summary>为 true 时忽略取件输入（背人期间由 ClimbController 关掉）</summary>
    [System.NonSerialized] public bool InputLocked;

    public bool IsCharging => charging;

    /// <summary>当前是否在货架触发区内</summary>
    public bool IsAtRack => currentRack != null;

    /// <summary>蓄力进度 0~1，以后做蓄力条 UI 直接读这个</summary>
    public float ChargeRatio => Mathf.Clamp01(chargeTime / Mathf.Max(0.01f, MaxChargeTime));

    void Awake()
    {
        grab = GetComponent<GrabController>();
    }

    /// <summary>由 RackController 的触发区调用：进入 / 离开货架</summary>
    public void EnterRack(RackController rack)
    {
        if (rack != null) currentRack = rack;
    }

    public void ExitRack(RackController rack)
    {
        if (currentRack == rack) currentRack = null;
    }

    void Update()
    {
        // 取件不再直接读键盘：改由 PlayerInput 的 Grab 事件转到 OnTake，
        // 键盘玩家按 Q、手柄玩家按 A，各用各的设备，互不串键。

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

    /// <summary>
    /// 由 PlayerInput 的 Grab 事件调用：键盘玩家按 Q、手柄玩家按 A。
    /// 在货架触发区内按下拿取键时，取走离玩家站位最近的那件（和放置先后无关）。
    /// 手上装不下（超重 / 叠满）时不改动货架状态。
    /// </summary>
    public void OnTake(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;        // 只认按下那一刻
        if (InputLocked) return;           // 背人期间不允许取件
        if (currentRack == null) return;   // 不在货架触发区内

        currentRack.TryTakeToHands(grab, transform.position);
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

        if (!grab.IsHolding) return;

        // 在货架触发区内：只做「放置」，不再投掷。
        // 货架按长轴负向到正向找落点（先算好，放得下才真正从手里取走）。
        if (currentRack != null)
        {
            currentRack.TryPlaceFromHands(grab, transform.position);
            // 放不下（那块地方已经放满）：什么都不做，快递留在手上
            return;
        }

        // 不在货架旁：正常投掷
        Rigidbody rb = grab.Release();
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

        // 站在货架旁：投掷被「放置」取代，改成预览会落到哪一段
        if (currentRack != null)
        {
            Rigidbody top = grab.TopBody;
            if (top != null &&
                currentRack.TryPreviewSpot(top, transform.position, out Vector3 spot, out float len))
            {
                Vector3 axis = currentRack.transform.right * (len * 0.5f);
                Gizmos.color = new Color(0.2f, 1f, 1f, 0.95f);
                Gizmos.DrawWireSphere(spot, 0.035f);
                Gizmos.DrawLine(spot - axis, spot + axis);
            }
            return;
        }

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
