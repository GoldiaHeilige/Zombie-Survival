using UnityEngine;

namespace Game.Combat.Weapon.Recoil
{
    public class RecoilController : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private CameraRecoilDriver cameraDriver; // nên kéo FPS camera vào đây
        [SerializeField] private Transform weaponViewKick;        // optional: pivot viewmodel để nảy

        // Profile nội bộ (không hiện Inspector)
        private RecoilProfile profile;

        private RecoilProfile.ViewmodelKickSettings vmKickSettings;
        private RecoilProfile.ViewmodelRecoilSwaySettings vmSwaySettings;

        // runtime
        Vector3 vmBaseLocalPos;
        Vector3 vmBaseLocalEuler;
        Vector3 vmRecoilPos, vmRecoilPosVel;
        Vector3 vmRecoilEuler, vmRecoilEulerVel;

        [Header("Debug (read-only)")]
        [SerializeField] private string profileName;
        [SerializeField] private float accumVert;
        [SerializeField] private float accumHorz;
        [SerializeField] private float lastShotTime = -999f;
        [SerializeField] private bool isRecovering;
        [SerializeField] private float recoveryTimer;
        [SerializeField] private int altSign = 1;
        [SerializeField] private int burstIndex = 0;

        private float vmKickZ;
        private float vmKickVelZ;
        private bool vmActive;
        [SerializeField] private float vmEpsilon = 1e-4f;

        public float AccumVertical => accumVert;
        public float AccumHorizontal => accumHorz;
        public bool IsRecovering => isRecovering;
        public string CurrentProfileName => profileName;

        int _authorizedFrame = -1;


        public void Bind(RecoilProfile newProfile, CameraRecoilDriver driver = null, Transform viewKickPivot = null)
        {
            profile = newProfile;
            if (driver != null) cameraDriver = driver;
            if (viewKickPivot != null) weaponViewKick = viewKickPivot;

            // cache settings từ profile để tránh đọc ScriptableObject mỗi frame
            vmKickSettings = profile != null ? profile.ViewmodelKick : default;
            vmSwaySettings = profile != null ? profile.ViewmodelSway : default;

            ResetState();

            profileName = profile ? profile.ProfileName : "<none>";
            enabled = profile != null;
        }


        public void AuthorizeShotThisFrame()
        {
            _authorizedFrame = Time.frameCount;
            /*Debug.Log($"[Recoil] AUTH frame={_authorizedFrame} id={GetInstanceID()}");*/
        }

        public void ResetState()
        {
            accumVert = accumHorz = 0f;
            lastShotTime = -999f;
            recoveryTimer = 0f;
            isRecovering = false;
            altSign = 1;
            burstIndex = 0;
            vmKickZ = vmKickVelZ = 0f;
            vmActive = false;

            vmRecoilPos = vmRecoilPosVel = Vector3.zero;
            vmRecoilEuler = vmRecoilEulerVel = Vector3.zero;

            if (weaponViewKick != null)
            {
                vmBaseLocalPos = weaponViewKick.localPosition;
                vmBaseLocalEuler = weaponViewKick.localEulerAngles;
            }
        }

        public void OnShot(bool isADS, float dtSinceLastShot)
        {
            /* Debug.Log($"[Recoil] OnShot() frame={Time.frameCount} auth={_authorizedFrame} id={GetInstanceID()}");*/

            if (Time.frameCount != _authorizedFrame)
            {
                Debug.LogWarning(
                    "[Recoil] OnShot() bị gọi mà không qua commit-frame -> BỎ QUA\n" +
                    System.Environment.StackTrace
                );
                return;
            }


            if (!profile) return;

            // viewmodel recoil sway per-shot (đọc từ profile)
            if (vmSwaySettings.Enable && weaponViewKick != null)
            {
                var posKick = isADS ? vmSwaySettings.ADSRecoilPos : vmSwaySettings.HipRecoilPos;
                var rotKick = isADS ? vmSwaySettings.ADSRecoilEuler : vmSwaySettings.HipRecoilEuler;

                vmRecoilPos += posKick;
                vmRecoilEuler += rotKick;
            }


            if (Time.time - lastShotTime > Mathf.Max(0.01f, profile.ShotImpulse.BurstResetCooldown))
            {
                burstIndex = 0;
                altSign = 1;
            }

            if (isADS)
            {
                bool firstInBurst = (burstIndex == 0);
                float v = profile.GetADSVerticalPerShot(firstInBurst);

                // ----- Horizontal recoil: dùng curve nếu có, fallback sang impulse + randomness -----
                float h = 0f;
                float maxH = Mathf.Max(0f, profile.ShotImpulse.MaxHorizontal);

                var patternCurve = profile.ShotImpulse.HorizontalPatternCurve;
                int patternSteps = profile.ShotImpulse.HorizontalPatternSteps;
                bool hasCurve = patternCurve != null &&
                                patternCurve.keys != null &&
                                patternCurve.keys.Length > 0 &&
                                patternSteps > 1 &&
                                maxH > 0f;

                if (hasCurve)
                {
                    // burstIndex: 0,1,2,... map sang t [0..1]
                    float t = Mathf.Clamp01(burstIndex / (float)(patternSteps - 1));

                    // curve Y: mong muốn offset ngang chuẩn hoá -1..1
                    float baseNorm = patternCurve.Evaluate(t);
                    baseNorm = Mathf.Clamp(baseNorm, -1f, 1f);

                    // scale theo MaxHorizontal
                    float target = baseNorm * maxH;

                    // thêm noise nhẹ quanh path
                    float randomness = Mathf.Clamp01(profile.ShotImpulse.HorizontalRandomness);
                    if (randomness > 0f && maxH > 0f)
                    {
                        // jitterNorm ∈ [-r, r], jitter tầm 20% MaxHorizontal khi r=1
                        float jitterNorm = Random.Range(-1f, 1f) * randomness;
                        target += jitterNorm * maxH * 0.2f;
                    }

                    // h = delta cần thêm vào accumHorz để đi tới vị trí target theo curve
                    h = target - accumHorz;
                }
                else
                {
                    // Fallback: kiểu cũ (impulse mỗi viên), nhưng BỎ AlternateHorizontal
                    float hBase = profile.GetADSHorizontalPerShot();
                    h = hBase;

                    float randomness = Mathf.Clamp01(profile.ShotImpulse.HorizontalRandomness);
                    if (Mathf.Abs(h) > 0f && randomness > 0f)
                    {
                        // jitter ∈ [1 - r, 1 + r]
                        float jitter = 1f + Random.Range(-randomness, randomness);
                        h *= jitter;
                    }
                }

                // ----- Cộng vào tích luỹ -----
                accumVert = Mathf.Clamp(
                    accumVert + Mathf.Max(0f, v),
                    0f,
                    Mathf.Max(0f, profile.ShotImpulse.MaxVertical)
                );

                if (maxH > 0f)
                {
                    accumHorz = Mathf.Clamp(
                        accumHorz + h,
                        -maxH,
                        maxH
                    );
                }
                else
                {
                    accumHorz += h;
                }

                // Viewmodel kick ADS giữ nguyên như cũ
                if (profile.Output.ApplyToWeaponKick && profile.Output.WeaponKickScale > 0f && vmKickSettings.KickImpulseADS > 0f)
                {
                    float impulse = vmKickSettings.KickImpulseADS * profile.Output.WeaponKickScale;
                    float dir = vmKickSettings.BackwardIsNegativeZ ? -1f : +1f;
                    vmKickVelZ += dir * impulse;
                    vmActive = true;
                }

                isRecovering = false;
                recoveryTimer = 0f;
                burstIndex++;
            }
            else
            {
                // HIP: giữ logic cũ (nếu có), chỉ viewmodel kick, không áp dụng camera recoil
                if (profile.Output.ApplyToWeaponKick && profile.Output.WeaponKickScale > 0f && vmKickSettings.KickImpulseHip > 0f)
                {
                    float impulse = vmKickSettings.KickImpulseHip * profile.Output.WeaponKickScale;
                    float dir = vmKickSettings.BackwardIsNegativeZ ? -1f : +1f;
                    vmKickVelZ += dir * impulse;
                    vmActive = true;
                }

                isRecovering = false;
                recoveryTimer = 0f;
                // burstIndex vẫn tăng để hipfire cũng đi theo cùng pattern nếu bạn muốn,
                // hoặc nếu không thích thì có thể không tăng trong nhánh này.
                burstIndex++;
            }

            lastShotTime = Time.time;
        }

        public void Tick(float dt, bool isFiring = false, bool isADS = false)
        {
            if (!profile) return;

            // ===== Khối hồi recoil tích lũy (camera) =====
            if (!isFiring)
            {
                if (!isRecovering)
                {
                    recoveryTimer += dt;
                    if (recoveryTimer >= Mathf.Max(0f, profile.Recovery.RecoveryDelay))
                    {
                        isRecovering = true;
                        recoveryTimer = 0f;
                    }
                }
                else
                {
                    float baseSpeed = Mathf.Max(0f, profile.Recovery.RecoverySpeed);
                    float t = Mathf.Clamp01(recoveryTimer / 1.0f);
                    float curve = (profile.Recovery.RecoveryCurve != null)
                        ? profile.Recovery.RecoveryCurve.Evaluate(t)
                        : 1f;

                    float step = baseSpeed * curve * dt;

                    accumVert = MoveTowardZero(accumVert, step);
                    accumHorz = MoveTowardZero(accumHorz, step);

                    recoveryTimer += dt;

                    if (Mathf.Abs(accumVert) < 0.001f && Mathf.Abs(accumHorz) < 0.001f)
                    {
                        accumVert = accumHorz = 0f;
                        isRecovering = false;
                        recoveryTimer = 0f;
                    }
                }
            }
            else
            {
                isRecovering = false;
                recoveryTimer = 0f;
            }

            if (profile.HasPitchClamp(out float minP, out float maxP))
                accumVert = Mathf.Clamp(accumVert, minP, maxP);

            // ✅ Chỉ áp dụng camera recoil khi đang ADS
            if (profile.Output.ApplyToCamera && cameraDriver)
            {
                if (isADS)
                {
                    // 🔒 chỉ update nếu thực sự có tích luỹ > epsilon
                    if (Mathf.Abs(accumHorz) > 0.0001f || Mathf.Abs(accumVert) > 0.0001f)
                        cameraDriver.SetRecoilOffsets(accumHorz, accumVert);
                    else
                        cameraDriver.SetRecoilOffsets(0f, 0f);
                }
                else
                {
                    cameraDriver.SetRecoilOffsets(0f, 0f);
                }
            }

            if (profile.Output.ApplyToWeaponKick && weaponViewKick)
            {
                // 1) Cập nhật spring Z như cũ
                if (vmActive)
                {
                    float k = Mathf.Max(1f, vmKickSettings.SpringK);
                    float dmp = Mathf.Max(0f, vmKickSettings.Damping);

                    float force = -k * vmKickZ - dmp * vmKickVelZ;
                    vmKickVelZ += force * dt;
                    vmKickZ += vmKickVelZ * dt;

                    vmKickZ = Mathf.Clamp(vmKickZ, -vmKickSettings.MaxKick, vmKickSettings.MaxKick);
                }

                // 2) Hồi recoil sway về 0
                if (vmSwaySettings.Enable)
                {
                    vmRecoilPos = Vector3.SmoothDamp(
                        vmRecoilPos, Vector3.zero, ref vmRecoilPosVel,
                        1f / Mathf.Max(1f, vmSwaySettings.PosReturnSpeed),
                        Mathf.Infinity, dt);

                    vmRecoilEuler.x = Mathf.SmoothDampAngle(
                        vmRecoilEuler.x, 0f, ref vmRecoilEulerVel.x,
                        1f / Mathf.Max(1f, vmSwaySettings.RotReturnSpeed),
                        Mathf.Infinity, dt);
                    vmRecoilEuler.y = Mathf.SmoothDampAngle(
                        vmRecoilEuler.y, 0f, ref vmRecoilEulerVel.y,
                        1f / Mathf.Max(1f, vmSwaySettings.RotReturnSpeed),
                        Mathf.Infinity, dt);
                    vmRecoilEuler.z = Mathf.SmoothDampAngle(
                        vmRecoilEuler.z, 0f, ref vmRecoilEulerVel.z,
                        1f / Mathf.Max(1f, vmSwaySettings.RotReturnSpeed),
                        Mathf.Infinity, dt);
                }

                // 3) Tính pose target từ base + offsets
                if (weaponViewKick)
                {
                    Vector3 targetPos = vmBaseLocalPos;
                    targetPos.z += vmKickZ;
                    targetPos += vmRecoilPos;

                    weaponViewKick.localPosition = Vector3.Lerp(
                        weaponViewKick.localPosition, targetPos, vmKickSettings.PositionLerp);

                    Vector3 targetEuler = vmBaseLocalEuler + vmRecoilEuler;
                    weaponViewKick.localRotation = Quaternion.Euler(targetEuler);
                }

                // 4) Khi spring Z đã yên thì tắt vmActive
                if (vmActive && Mathf.Abs(vmKickZ) < vmEpsilon && Mathf.Abs(vmKickVelZ) < vmEpsilon)
                {
                    vmKickZ = vmKickVelZ = 0f;
                    vmActive = false;
                }
            }
        }


        /// <summary>Random spread vòng tròn (deg) cho hipfire: trả Vector2(yaw, pitch).</summary>
        public Vector2 GetHipfireSpreadDeg(bool isMoving, bool isAirborne, bool isCrouching)
        {
            if (!profile) return Vector2.zero;

            // Lấy base spread từ profile (đã có yaw/pitch tách biệt)
            Vector2 baseSpread = profile.GetHipfireSpreadDeg(isMoving, isAirborne, isCrouching);

            // Random trong phạm vi ellipse (yaw và pitch độc lập)
            Vector2 spread = new Vector2(
                Random.Range(-1f, 1f) * baseSpread.x,
                Random.Range(-1f, 1f) * baseSpread.y
            );

            return spread;
        }

        /// <summary>
        /// Tính hướng bắn đã áp cone (dùng cho HITSCan/Projectile).
        /// - origin.forward là hướng cơ sở (thường là FPS camera forward).
        /// - yaw/pitch theo độ, pitch dương = ngóc UP => Quaternion.Euler(-pitch, yaw, 0).
        /// </summary>
        public Vector3 ComputeShotDirection(Transform origin, bool isADS,
                                            bool isMoving, bool isAirborne, bool isCrouching)
        {
            if (!origin) return Vector3.forward;

            if (isADS)
                return origin.forward.normalized;

            Vector2 yawPitch = GetHipfireSpreadDeg(isMoving, isAirborne, isCrouching);

            // local offset rồi biến về world theo rotation của camera
            Quaternion localOffset = Quaternion.Euler(-yawPitch.y, yawPitch.x, 0f);
            Vector3 dir = origin.rotation * (localOffset * Vector3.forward);

            return dir.normalized;
        }


        /// <summary>Vẽ debug ray cho 1 phát bắn (để thấy hipfire cone đang hoạt động).</summary>
        public void DebugDrawShotRay(Transform origin, bool isADS,
                                     bool isMoving, bool isAirborne, bool isCrouching,
                                     float length = 50f, float seconds = 0.25f)
        {
            Vector3 d = ComputeShotDirection(origin, isADS, isMoving, isAirborne, isCrouching);
            Debug.DrawRay(origin.position, d * length, isADS ? Color.cyan : Color.yellow, seconds);
        }

        // ===== util =====
        static float MoveTowardZero(float v, float step)
        {
            if (v > 0f) { v -= step; if (v < 0f) v = 0f; }
            else if (v < 0f) { v += step; if (v > 0f) v = 0f; }
            return v;
        }

#if UNITY_EDITOR
        void OnValidate() { profileName = profile ? profile.ProfileName : "<none>"; }
#endif
    

    public void SnapToNeutralPose()
        {
            // zero runtime
            vmKickZ = vmKickVelZ = 0f;
            vmActive = false;

            vmRecoilPos = vmRecoilPosVel = Vector3.zero;
            vmRecoilEuler = vmRecoilEulerVel = Vector3.zero;

            accumVert = accumHorz = 0f;
            isRecovering = false;
            recoveryTimer = 0f;

            // snap transform về base đã cache
            if (weaponViewKick != null)
            {
                weaponViewKick.localPosition = vmBaseLocalPos;
                weaponViewKick.localRotation = Quaternion.Euler(vmBaseLocalEuler);
            }

            // optional: camera recoil về 0 cho chắc
            if (cameraDriver != null)
                cameraDriver.SetRecoilOffsets(0f, 0f);
        }

    }
}