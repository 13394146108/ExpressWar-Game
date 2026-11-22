using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;
using UnityEngine.InputSystem;

public class MovePlayerController : MonoBehaviour
{
    [Header("速度")]
    public float Speed = 5;
    
    [Header("转向")]
    [Tooltip("每秒最大转动角度；设为 0 表示立即朝向移动方向")]
    public float TurnSpeed = 720f;

    private Vector2 MovementInput;

    // Update is called once per frame
    void Update()
    {
        Vector3 moveDir = new Vector3(MovementInput.x,0,MovementInput.y);
        if (moveDir.sqrMagnitude > 1)
        {
            moveDir.Normalize();
        }
        transform.Translate(moveDir * Speed * Time.deltaTime,Space.World);
        RotationTo(moveDir);
    }

    void RotationTo(Vector3 moveDir)
    {
        if (moveDir.sqrMagnitude < 0.00001)
        {
            return;
        }
        Quaternion target = Quaternion.LookRotation(moveDir,Vector3.up);
        transform.rotation = TurnSpeed <= 0f ? target : Quaternion.RotateTowards(transform.rotation,target,TurnSpeed * Time.deltaTime);
    }
    public void OnMove(InputAction.CallbackContext ctx)
    {
        MovementInput = ctx.ReadValue<Vector2>();
    }
}
