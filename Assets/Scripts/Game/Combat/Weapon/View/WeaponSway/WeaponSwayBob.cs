using UnityEngine;

[DefaultExecutionOrder(8100)]
[DisallowMultipleComponent]
public class WeaponSwayBob : MonoBehaviour
{
    // =============================
    // EXTERNAL INPUT
    // =============================
    Vector2 lookDelta;
    Vector3 externalVelocity;
    float adsWeight;

    Vector3 smoothedVelocity;
    Vector3 velSmoothRef;

    // =============================
    // CONFIG (COD STYLE – OPTION A)
    // =============================

    // ---------- LOOK SWAY ----------
    [Header("Look Sway")]
    public Vector3 lookPosSway = new Vector3(0.0035f, 0.0020f, 0.0012f);
    public Vector3 lookRotSway = new Vector3(0.75f, 0.55f, 0.25f); // pitch,yaw,roll

    public Vector3 lookPosClamp = new Vector3(0.03f, 0.015f, 0.015f);
    public Vector3 lookRotClamp = new Vector3(4.5f, 3f, 2.0f);

    // ---------- MOVE SWAY ----------
    [Header("Move Sway")]
    public Vector3 movePosSway = new Vector3(0.02f, 0.016f, 0.012f);
    public Vector3 moveRotSway = new Vector3(0.75f, 0.55f, 0.35f);

    public Vector3 movePosClamp = new Vector3(0.065f, 0.05f, 0.03f);
    public Vector3 moveRotClamp = new Vector3(5.2f, 3.8f, 2.5f);

    // ---------- BOB ----------
    [Header("Bob")]
    public float bobFrequency = 6.2f;
    public Vector3 bobAmplitudePos = new Vector3(0.0035f, 0.0105f, 0.0012f);
    public Vector3 bobAmplitudeRot = new Vector3(0.65f, 0.22f, 0.28f);

    // ---------- LANDING ----------
    [Header("Landing")]
    public float landingAmount = 0.08f;
    public float landingSmoothTime = 0.15f;

    // ---------- SPRINT STYLE ----------
    [Header("Sprint Offset")]
    [Tooltip("Offset thêm khi đang chạy (sprint) – chỉnh cho súng nghiêng sang trái, hơi tụt xuống, xoay Y/Roll tùy ý.")]
    public Vector3 sprintPosOffset = new Vector3(-0.03f, -0.015f, 0.0f);
    public Vector3 sprintRotOffset = new Vector3(0.0f, -10.0f, 3.0f); // pitch, yaw, roll
                                                                      // ---------- SPRINT EXTRA SWAY ----------
    [Header("Sprint Sway")]
    [Tooltip("Nhân độ lắc trái-phải khi đang chạy (Sprint)")]
    public float sprintMoveSideMult = 1.2f;   // cho phần sway theo velocity
    public float sprintBobSideMult = 1.6f;   // cho phần bob trái-phải

    // ---------- RETURN ----------
    [Header("Return / Smoothing")]
    public float posReturnSpeed = 12f;
    public float rotReturnSpeed = 12f;

    [Header("Firing Sway")]
    [Tooltip("Scale cho move sway khi đang bắn (0 = tắt hẳn).")]
    [Range(0f, 1f)] public float firingMoveSwayScale = 0f;

    [Tooltip("Scale cho bob sway khi đang bắn (0 = tắt hẳn).")]
    [Range(0f, 1f)] public float firingBobSwayScale = 0f;

    [SerializeField] Transform camAnchor;

    // Runtime
    float bobPhase;
    Vector3 targetPos;
    Vector3 targetEuler;

    Vector3 posVel, rotVel;
    bool isSprinting;
    bool isFiring;

    float landingOffset;
    float landingVel;

    [Header("Landing Detection")]
    [SerializeField] float landingMinFallSpeed = 4f;     // rơi nhanh hơn giá trị này mới tính là “rơi”
    [SerializeField] float landingGroundEps = 0.1f;    // gần 0 coi như chạm đất
    float _prevVelY;

    // API
    public void SetLookDelta(Vector2 d) => lookDelta = d;
    public void SetExternalVelocity(Vector3 v) => externalVelocity = v;
    public void SetADSWeight(float w)
    {
        adsWeight = Mathf.Clamp01(w);
    }

    public void SetIsSprinting(bool s) => isSprinting = s;   // <<< NEW
    public void SetIsFiring(bool firing) => isFiring = firing;

    void LateUpdate()
    {
        // Nếu đang pause / bị chặn look (PauseMenu dùng Full) -> không sway nữa
        if (InputBlockerSystem.Has(InputBlocker.Full) || InputBlockerSystem.Has(InputBlocker.CameraLook))
        {
            float udt = Time.unscaledDeltaTime;

            // ép input về 0 để không “giật” khi unpause
            lookDelta = Vector2.zero;
            externalVelocity = Vector3.zero;
            isSprinting = false;
            isFiring = false;

            // trả dần về pose gốc bằng unscaled dt (vẫn chạy khi timescale = 0)
            Vector3 blockedNewPos = Vector3.SmoothDamp(
                transform.localPosition, Vector3.zero,
                ref posVel, 1f / posReturnSpeed, Mathf.Infinity, udt
            );

            Vector3 blockedNewEuler = new Vector3(
                Mathf.SmoothDampAngle(transform.localEulerAngles.x, 0f, ref rotVel.x, 1f / rotReturnSpeed, Mathf.Infinity, udt),
                Mathf.SmoothDampAngle(transform.localEulerAngles.y, 0f, ref rotVel.y, 1f / rotReturnSpeed, Mathf.Infinity, udt),
                Mathf.SmoothDampAngle(transform.localEulerAngles.z, 0f, ref rotVel.z, 1f / rotReturnSpeed, Mathf.Infinity, udt)
            );

            transform.localPosition = blockedNewPos;
            transform.localRotation = Quaternion.Euler(blockedNewEuler);
            return;
        }

        float dt = Time.deltaTime;

        float currentVelY = externalVelocity.y;
        bool landedThisFrame =
            (_prevVelY < -landingMinFallSpeed) &&   // frame trước rơi đủ nhanh
            (currentVelY > -landingGroundEps);      // frame hiện tại gần 0 / dương nhẹ
        _prevVelY = currentVelY;

        // -----------------------------------------
        // Smooth velocity (COD style = low inertia)
        // -----------------------------------------
        smoothedVelocity = Vector3.SmoothDamp(
            smoothedVelocity, externalVelocity,
            ref velSmoothRef, 0.06f
        );

        float swayScale = 1f;
        float bobScale = 1f;
        if (isFiring)
            bobScale *= firingBobSwayScale;

        // ======================================================
        // 1) LOOK SWAY (full XYZ pos + full rotation roll)
        // ======================================================
        Vector3 posLook = new Vector3(
            -lookDelta.x * lookPosSway.x,
            -lookDelta.y * lookPosSway.y,
            -lookDelta.x * lookPosSway.z
        ) * swayScale;

        Vector3 rotLook = new Vector3(
            -lookDelta.y * lookRotSway.x, // pitch
             lookDelta.x * lookRotSway.y, // yaw
             lookDelta.x * lookRotSway.z  // roll
        ) * swayScale;

        posLook = Vector3.Min(posLook, lookPosClamp);
        posLook = Vector3.Max(posLook, -lookPosClamp);

        rotLook = Vector3.Min(rotLook, lookRotClamp);
        rotLook = Vector3.Max(rotLook, -lookRotClamp);


        // ======================================================
        // 2) MOVE SWAY (local velocity full XYZ)
        // ======================================================
        Vector3 vel = smoothedVelocity;
        Vector3 localVel = transform.parent
            ? transform.parent.InverseTransformDirection(vel)
            : vel;

        Vector3 posMove = Vector3.zero;
        Vector3 rotMove = Vector3.zero;

        if (!isFiring)
        {
            posMove = new Vector3(
                -localVel.x * movePosSway.x,
                -localVel.z * movePosSway.y,
                -localVel.x * movePosSway.z
            ) * swayScale;

            rotMove = new Vector3(
                localVel.z * moveRotSway.x,   // pitch
                localVel.x * moveRotSway.y,   // yaw
                localVel.x * moveRotSway.z    // roll
            ) * swayScale;

            if (isSprinting)
            {
                posMove.x *= sprintMoveSideMult;
                rotMove.y *= sprintMoveSideMult;
                rotMove.z *= sprintMoveSideMult;
            }

            posMove = Vector3.ClampMagnitude(posMove, movePosClamp.magnitude);
            rotMove = Vector3.ClampMagnitude(rotMove, moveRotClamp.magnitude);
        }


        // ======================================================
        // 3) BOB (Pos + Rot, full XYZ)
        // ======================================================
        float speed = new Vector3(vel.x, 0, vel.z).magnitude;

        // COD-style bob speed
        float freq =
            (speed <= 0.1f) ? bobFrequency * 0.45f :
            (speed < 3f) ? bobFrequency * Mathf.Lerp(0.65f, 1.0f, speed / 3f) :
                              bobFrequency * Mathf.Lerp(1.0f, 2.25f, (speed - 3f) / 3f);

        bobPhase += freq * dt;

        Vector3 posBob = Vector3.zero;
        Vector3 rotBob = Vector3.zero;

        if (!isFiring)
        {
            posBob = new Vector3(
                Mathf.Sin(bobPhase * 1.0f) * bobAmplitudePos.x,
                Mathf.Abs(Mathf.Sin(bobPhase * 1.3f)) * bobAmplitudePos.y,
                Mathf.Sin(bobPhase * 0.8f) * bobAmplitudePos.z
            ) * bobScale;

            rotBob = new Vector3(
                Mathf.Sin(bobPhase * 1.1f) * bobAmplitudeRot.x, // pitch
                Mathf.Sin(bobPhase * 0.9f) * bobAmplitudeRot.y, // yaw
                Mathf.Sin(bobPhase * 0.6f) * bobAmplitudeRot.z  // roll
            ) * bobScale;

            if (isSprinting)
            {
                posBob.x *= sprintBobSideMult;
                rotBob.y *= sprintBobSideMult;
                rotBob.z *= sprintBobSideMult;
            }
        }


        // ======================================================
        // 4) LANDING BOB
        // ======================================================
        if (landedThisFrame)
            landingOffset = -landingAmount;

        landingOffset = Mathf.SmoothDamp(
            landingOffset, 0,
            ref landingVel, landingSmoothTime
        );

        posBob.y += landingOffset;

        // ======================================================
        // 5) Combine Everything
        // ======================================================
        Vector3 finalPos = posLook + posMove + posBob;
        Vector3 finalRot = rotLook + rotMove + rotBob;

        if (isSprinting)
        {
            finalPos += sprintPosOffset;
            finalRot += sprintRotOffset;
        }
        targetPos = finalPos;
        targetEuler = finalRot;


        // ======================================================
        // 6) Smooth to Target
        // ======================================================
        Vector3 newPos = Vector3.SmoothDamp(
            transform.localPosition, targetPos,
            ref posVel, 1f / posReturnSpeed, Mathf.Infinity, dt
        );

        Vector3 newEuler = new Vector3(
            Mathf.SmoothDampAngle(transform.localEulerAngles.x, targetEuler.x, ref rotVel.x, 1f / rotReturnSpeed, Mathf.Infinity, dt),
            Mathf.SmoothDampAngle(transform.localEulerAngles.y, targetEuler.y, ref rotVel.y, 1f / rotReturnSpeed, Mathf.Infinity, dt),
            Mathf.SmoothDampAngle(transform.localEulerAngles.z, targetEuler.z, ref rotVel.z, 1f / rotReturnSpeed, Mathf.Infinity, dt)
        );

        transform.localPosition = newPos;
        transform.localRotation = Quaternion.Euler(newEuler);
    }
}
