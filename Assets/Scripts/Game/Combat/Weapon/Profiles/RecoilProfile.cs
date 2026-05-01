using UnityEngine;

namespace Game.Combat.Weapon.Recoil
{
    [CreateAssetMenu(fileName = "RecoilProfile", menuName = "Game/Weapons/Recoil Profile")]
    public class RecoilProfile : ScriptableObject
    {
        [Header("Identification")]
        [Tooltip("Tên hiển thị nội bộ để debug UI.")]
        public string ProfileName = "Default";

        // =========================================
        // A) XUNG MỖI VIÊN (ADS)
        // =========================================
        [System.Serializable]
        public struct ShotImpulseSettings
        {
            [Header("Per-shot Impulse (ADS)")]
            [Tooltip("Độ giật dọc mỗi viên (độ).")]
            [Min(0f)] public float VerticalPerShot;

            [Tooltip("Độ giật ngang mỗi viên (độ). Dương = qua phải, âm = qua trái.")]
            public float HorizontalPerShot;

            [Tooltip("Giới hạn tích luỹ dọc trong một chuỗi bắn (độ).")]
            [Min(0f)] public float MaxVertical;

            [Tooltip("Giới hạn tích luỹ ngang trong một chuỗi bắn (độ).")]
            [Min(0f)] public float MaxHorizontal;

            [Header("Burst logic")]
            [Tooltip("Hệ số nhân cho viên đầu tiên trong chuỗi (>= 1.0 = giật mạnh hơn).")]
            [Min(0f)] public float FirstShotMultiplier;

            [Tooltip("Nếu ngừng bắn lâu hơn thời gian này (s) thì reset chuỗi.")]
            [Min(0f)] public float BurstResetCooldown;

            [Header("Horizontal Pattern (Curve)")]
            [Tooltip("Curve mô tả tổng offset ngang (chuẩn hoá) theo số viên trong burst. " +
                     "Trục X: 0..1 (tỷ lệ số viên trong pattern), trục Y: -1..1 (offset chuẩn hoá).")]
            public AnimationCurve HorizontalPatternCurve;

            [Min(0)]
            [Tooltip("Số viên dùng để quét hết curve từ 0..1. 0 hoặc 1 = không dùng curve, fallback sang impulse cũ.")]
            public int HorizontalPatternSteps;


            [Header("Horizontal Randomness")]
            [Range(0f, 1f)]
            [Tooltip("Mức random cho biên độ giật ngang. 0 = mỗi viên giống nhau; 1 = ±100% quanh giá trị gốc.")]
            public float HorizontalRandomness;
        }

        // =========================================
        // B) PHỤC HỒI (RECOVERY)
        // =========================================
        [System.Serializable]
        public struct RecoverySettings
        {
            [Header("Recovery Core")]
            [Tooltip("Độ trễ trước khi bắt đầu hồi sau khi nhả cò (s).")]
            [Min(0f)] public float RecoveryDelay;

            [Tooltip("Tốc độ hồi về 0 (độ/giây).")]
            [Min(0f)] public float RecoverySpeed;

            [Header("Recovery Shape")]
            [Tooltip("Đường cong điều chế tốc độ hồi (trục X: 0..1 thời gian hồi, Y: 0..1 hệ số). Bỏ trống = tuyến tính.")]
            public AnimationCurve RecoveryCurve;
        }

        // =========================================
        // C) HIPFIRE SPREAD - ĐÃ SỬA: TÁCH YAWN/PITCH
        // =========================================
        [System.Serializable]
        public struct HipfireSettings
        {
            [Header("Hipfire Spread (Cone)")]
            [Tooltip("Spread ngang (yaw, độ) khi đứng yên.")]
            [Min(0f)] public float BaseSpreadYaw;

            [Tooltip("Spread dọc (pitch, độ) khi đứng yên.")]
            [Min(0f)] public float BaseSpreadPitch;

            [Tooltip("Nhân vào spread khi đang di chuyển trên mặt đất.")]
            [Min(0f)] public float MoveSpreadMultiplier;

            [Tooltip("Nhân vào spread khi ở trên không (nhảy/airborne).")]
            [Min(0f)] public float AirSpreadMultiplier;

            [Tooltip("Nhân vào spread khi ngồi/crouch.")]
            [Min(0f)] public float CrouchSpreadMultiplier;

            [Header("Advanced")]
            [Tooltip("Nếu bật, crouch multiplier chỉ áp dụng cho vertical (pitch).")]
            public bool CrouchOnlyAffectsVertical;
        }

        // =========================================
        // D) HỆ SỐ THEO TRẠNG THÁI (ADS scale)
        // =========================================
        [System.Serializable]
        public struct StateScales
        {
            [Header("ADS Impulse Scales")]
            [Tooltip("Hệ số nhân dọc khi ADS (thường ~1.0–1.2).")]
            public float ADSVerticalScale;

            [Tooltip("Hệ số nhân ngang khi ADS.")]
            public float ADSHorizontalScale;

            [Header("Optional – siết ngang theo thời gian giữ ADS")]
            [Range(0f, 1f)]
            [Tooltip("0 = không siết; 1 = siết tối đa theo logic runtime tuỳ bạn.")]
            public float SprayTightenOverTime;
        }

        // =========================================
        // E) ÁP DỤNG LÊN CAMERA / VIEWMODEL
        // =========================================
        [System.Serializable]
        public struct OutputSettings
        {
            [Header("Camera")]
            [Tooltip("Áp recoil vào camera (pitch/yaw).")]
            public bool ApplyToCamera;

            [Tooltip("Giới hạn tổng pitch nhìn lên/xuống (deg). Bật clamp nếu min < max.")]
            public float ClampPitchMin;
            public float ClampPitchMax;

            [Header("Weapon Viewmodel Kick")]
            [Tooltip("Áp recoil vào viewmodel (nẩy súng).")]
            public bool ApplyToWeaponKick;

            [Tooltip("Hệ số nẩy của viewmodel (tỉ lệ).")]
            [Min(0f)] public float WeaponKickScale;
        }

        // =========================================
        // F) VIEWMODEL KICK (SPRING TRANSLATION)
        // =========================================
        [System.Serializable]
        public struct ViewmodelKickSettings
        {
            [Header("Spring Impulse")]
            [Tooltip("Impulse khi ADS. Đặt 0 để tắt hoàn toàn (kể cả khi ApplyToWeaponKick bật).")]
            public float KickImpulseADS;

            [Tooltip("Impulse khi hipfire. Đặt 0 để tắt hoàn toàn (kể cả khi ApplyToWeaponKick bật).")]
            public float KickImpulseHip;

            [Header("Clamp & Spring")]
            [Tooltip("Giới hạn biên độ nảy tối đa của viewmodel (đơn vị local Z).")]
            public float MaxKick;

            [Tooltip("Hệ số đàn hồi (spring stiffness).")]
            public float SpringK;

            [Tooltip("Giảm chấn spring.")]
            public float Damping;

            [Header("Blend Position")]
            [Tooltip("Smoothing khi ghi localPosition.z (0..1). 0.2-0.35 là hợp lý.")]
            [Range(0.05f, 0.9f)]
            public float PositionLerp;

            [Header("Direction")]
            [Tooltip("Nếu true: backward là -Z; false: +Z.")]
            public bool BackwardIsNegativeZ;
        }

        // =========================================
        // G) VIEWMODEL RECOIL SWAY (POS/ROT OFFSET)
        // =========================================
        [System.Serializable]
        public struct ViewmodelRecoilSwaySettings
        {
            [Header("Toggle")]
            [Tooltip("Bật tắt hoàn toàn viewmodel recoil sway.")]
            public bool Enable;

            [Header("Offset pos thêm mỗi viên")]
            [Tooltip("Offset pos thêm mỗi viên khi hipfire.")]
            public Vector3 HipRecoilPos;

            [Tooltip("Offset pos thêm mỗi viên khi ADS.")]
            public Vector3 ADSRecoilPos;

            [Header("Offset rot (Euler deg) thêm mỗi viên")]
            [Tooltip("Offset rot thêm mỗi viên khi hipfire.")]
            public Vector3 HipRecoilEuler;

            [Tooltip("Offset rot thêm mỗi viên khi ADS.")]
            public Vector3 ADSRecoilEuler;

            [Header("Hồi về 0")]
            [Tooltip("Tốc độ hồi pos về 0.")]
            public float PosReturnSpeed;

            [Tooltip("Tốc độ hồi rot về 0.")]
            public float RotReturnSpeed;
        }

        [SerializeField] public ShotImpulseSettings ShotImpulse = Defaults.DefaultShotImpulse();
        [SerializeField] public RecoverySettings Recovery = Defaults.DefaultRecovery();
        [SerializeField] public HipfireSettings Hipfire = Defaults.DefaultHipfire();
        [SerializeField] public StateScales Scales = Defaults.DefaultScales();
        [SerializeField] public OutputSettings Output = Defaults.DefaultOutput();
        [SerializeField] public ViewmodelKickSettings ViewmodelKick = Defaults.DefaultViewmodelKick();
        [SerializeField] public ViewmodelRecoilSwaySettings ViewmodelSway = Defaults.DefaultViewmodelSway();

        // --------- Helper getters ----------
        public float GetADSVerticalPerShot(bool firstInBurst)
        {
            var baseVal = ShotImpulse.VerticalPerShot * Scales.ADSVerticalScale;
            return firstInBurst ? baseVal * Mathf.Max(0f, ShotImpulse.FirstShotMultiplier) : baseVal;
        }

        public float GetADSHorizontalPerShot()
        {
            return ShotImpulse.HorizontalPerShot * Scales.ADSHorizontalScale;
        }

        /// <summary>
        /// Tính spread radius hiện tại cho hipfire dựa vào trạng thái chuyển động.
        /// ĐÃ SỬA: Trả về Vector2 với (yaw, pitch)
        /// </summary>
        public Vector2 GetHipfireSpreadDeg(bool isMoving, bool isAirborne, bool isCrouching)
        {
            float yaw = Hipfire.BaseSpreadYaw;
            float pitch = Hipfire.BaseSpreadPitch;

            // Apply movement/air multipliers
            if (isAirborne)
            {
                yaw *= Mathf.Max(0f, Hipfire.AirSpreadMultiplier);
                pitch *= Mathf.Max(0f, Hipfire.AirSpreadMultiplier);
            }
            else if (isMoving)
            {
                yaw *= Mathf.Max(0f, Hipfire.MoveSpreadMultiplier);
                pitch *= Mathf.Max(0f, Hipfire.MoveSpreadMultiplier);
            }

            // Apply crouch multiplier
            if (isCrouching)
            {
                if (Hipfire.CrouchOnlyAffectsVertical)
                {
                    // Chỉ giảm vertical spread khi crouch
                    pitch *= Mathf.Max(0f, Hipfire.CrouchSpreadMultiplier);
                }
                else
                {
                    // Giảm cả horizontal và vertical
                    yaw *= Mathf.Max(0f, Hipfire.CrouchSpreadMultiplier);
                    pitch *= Mathf.Max(0f, Hipfire.CrouchSpreadMultiplier);
                }
            }

            return new Vector2(
                Mathf.Max(0f, yaw),
                Mathf.Max(0f, pitch)
            );
        }

        public bool HasPitchClamp(out float minPitch, out float maxPitch)
        {
            minPitch = Output.ClampPitchMin;
            maxPitch = Output.ClampPitchMax;
            return minPitch < maxPitch;
        }

        // =========================================
        // Defaults tiện khởi tạo - ĐÃ CẬP NHẬT
        // =========================================
        private static class Defaults
        {
            public static ShotImpulseSettings DefaultShotImpulse() => new ShotImpulseSettings
            {
                VerticalPerShot = 0.35f,
                HorizontalPerShot = 0.12f,
                MaxVertical = 6.0f,
                MaxHorizontal = 2.5f,
                FirstShotMultiplier = 1.15f,
                BurstResetCooldown = 0.25f,
                HorizontalRandomness = 0.25f
            };

            public static RecoverySettings DefaultRecovery() => new RecoverySettings
            {
                RecoveryDelay = 0.06f,
                RecoverySpeed = 12f,
                RecoveryCurve = AnimationCurve.EaseInOut(0, 0, 1, 1)
            };

            public static HipfireSettings DefaultHipfire() => new HipfireSettings
            {
                BaseSpreadYaw = 1.5f,      // Ngang
                BaseSpreadPitch = 0.9f,    // Dọc (nhỏ hơn 40%)
                MoveSpreadMultiplier = 1.6f,
                AirSpreadMultiplier = 2.0f,
                CrouchSpreadMultiplier = 0.75f,
                CrouchOnlyAffectsVertical = true  // Crouch chỉ giảm spread dọc
            };

            public static StateScales DefaultScales() => new StateScales
            {
                ADSVerticalScale = 1.0f,
                ADSHorizontalScale = 1.0f,
                SprayTightenOverTime = 0.0f
            };

            public static OutputSettings DefaultOutput() => new OutputSettings
            {
                ApplyToCamera = true,
                ClampPitchMin = -89f,
                ClampPitchMax = 89f,
                ApplyToWeaponKick = true,
                WeaponKickScale = 1.0f
            };

            public static ViewmodelKickSettings DefaultViewmodelKick() => new ViewmodelKickSettings
            {
                KickImpulseADS = 0.0015f,
                KickImpulseHip = 0.015f,
                MaxKick = 0.05f,
                SpringK = 35f,
                Damping = 12f,
                PositionLerp = 0.25f,
                BackwardIsNegativeZ = true
            };

            public static ViewmodelRecoilSwaySettings DefaultViewmodelSway() => new ViewmodelRecoilSwaySettings
            {
                Enable = true,
                HipRecoilPos = new Vector3(0.002f, -0.0015f, 0f),
                ADSRecoilPos = new Vector3(0.0012f, -0.0008f, 0f),
                HipRecoilEuler = new Vector3(2.5f, 1.5f, 3.0f),
                ADSRecoilEuler = new Vector3(1.2f, 0.7f, 2.0f),
                PosReturnSpeed = 18f,
                RotReturnSpeed = 24f
            };
        }
    }
}