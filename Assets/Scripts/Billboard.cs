using UnityEngine;

/// <summary>
/// World Space UI 完全正对相机（含俯仰角），俯视角下文字不会被透视压扁。
/// 挂在 Canvas 上。
/// </summary>
[DisallowMultipleComponent]
public class Billboard : MonoBehaviour
{
    [Tooltip("留空则自动使用 Camera.main")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("勾选后只绕 Y 轴转：抬头低头时文字保持竖直，但俯视角下会被压扁")]
    [SerializeField] private bool yAxisOnly = false;

    [Tooltip("勾选后按距离缩放，保证屏幕上的字号恒定")]
    [SerializeField] private bool keepScreenSize = false;
    [SerializeField] private float baseDistance = 15f;

    private Vector3 baseScale;

    private void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
        baseScale = transform.localScale;
    }

    private void LateUpdate()
    {
        if (targetCamera == null) return;

        if (yAxisOnly)
        {
            // 只绕 Y 轴：适合平视视角
            Vector3 toCamera = targetCamera.transform.position - transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(-toCamera, Vector3.up);
        }
        else
        {
            // 完全对齐相机：文字平面垂直于视线，俯视角下也是正的
            // 与 Screen Space - Camera 的朝向行为一致（Canvas 可见正面是本地 -Z）
            transform.rotation = targetCamera.transform.rotation;
        }

        if (keepScreenSize)
        {
            float d = Vector3.Distance(targetCamera.transform.position, transform.position);
            transform.localScale = baseScale * Mathf.Max(0.01f, d / baseDistance);
        }
    }
}
