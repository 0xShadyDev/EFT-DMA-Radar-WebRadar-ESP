using System.Text;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using System.Numerics;
using System.Timers;
using Offsets;
using Vmmsharp;
using System.Diagnostics;
using static Vmmsharp.VmmProcess;
using System.Net;
using OpenTK.Graphics.ES10;

namespace eft_dma_radar
{       
    public class Aimbot
    {
        private Config _config;  // Declare _config
        float aimbotFOV;
        private float       aimbotMaxDistance;
        private bool        aimbotClosest;
        private bool        aimbotHead;
        private bool        aimbotNeck;
        private bool        aimbotChest;
        private bool        aimbotPelvis;
        private bool        aimbotRightLeg;
        private bool        aimbotLeftLeg;
        private int         aimbotKey;
        private int         saAimbotKey;
        private bool        aimbotEnabled;
        private bool        saimbotEnabled;
        private bool enableAimPrediction;
        private CameraManager _cameraManager { get => Memory.CameraManager; }
        private ReadOnlyDictionary<string, Player> AllPlayers { get => Memory.Players; }    
        private bool InGame { get => Memory.InGame; }
        private static PlayerManager playerManager { get => Memory.PlayerManager; }
        private Player localPlayer { get => Memory.LocalPlayer; }
        public ulong MovementContext { get; set; }
        private bool boneCache = false;
        public Dictionary<PlayerBones, Transform> boneTransforms = new Dictionary<PlayerBones, Transform>();
        private int boneCounter;
        private ScatterReadMap boneScatterMap;
        public ulong boneMatrix;
        public List<ulong> BonePointers { get; } = new List<ulong>();        
        private DateTime lastBoneReadTime = DateTime.MinValue;
        private Player TargetPlayer;
        private readonly Vector2 screenCenter = new Vector2(1920 / 2f, 1080 / 2f);     
        public Aimbot()
        {            
            _config = Program.Config;
        }
        public void Initialize()
        {
            aimbotMaxDistance = _config.AimbotMaxDistance;
            aimbotFOV = _config.AimbotFOV;
            aimbotClosest = _config.AimbotClosest;
            aimbotHead = _config.AimbotHead;
            aimbotNeck = _config.AimbotNeck;
            aimbotChest = _config.AimbotChest;
            aimbotPelvis = _config.AimbotPelvis;
            aimbotRightLeg = _config.AimbotRightLeg;
            aimbotLeftLeg = _config.AimbotLeftLeg;
            aimbotKey = _config.AimbotKeybind;
            saAimbotKey = _config.SASilentAimKey;
            aimbotEnabled = _config.EnableAimbot;
            saimbotEnabled = _config.SAEnableAimbot;
            enableAimPrediction = _config.AimbotPrediction;
            Execute();
        }      
        public void Execute()
        {
            if (!aimbotEnabled && !saimbotEnabled)
            {
                return;
            }

            // Ensure a minimum delay between executions (16ms for ~60Hz refresh)
            if ((DateTime.Now - lastBoneReadTime).TotalMilliseconds < 16)
            {
                return;
            }

            if (!InGame || localPlayer == null || !localPlayer.IsAlive)
            {
                return;
            }

            bool aimbotHeld = InputManager.IsKeyDown((Keys)aimbotKey);
            bool saAimbotHeld = InputManager.IsKeyDown((Keys)saAimbotKey);

            if (aimbotHeld || saAimbotHeld)
            {
                Memory.CameraManager.UpdateViewMatrix();
                TargetPlayer = GetTargetPlayerOptimized();

                // Validate the target player
                if (TargetPlayer == null || !TargetPlayer.IsAlive)
                {
                    //Program.Log("No valid target found or target is dead.");
                    return;
                }

                // Read positions and velocities
                var targetBonePosition = ReadAllBonePositions(TargetPlayer);
                var FirePos = localPlayer.fireportPosition;

                // Validate positions
                if (FirePos == Vector3.Zero || targetBonePosition == Vector3.Zero)
                {
                    //Program.Log("Failed to retrieve valid positions for target.");
                    return;
                }

                Vector3 targetPosition;
                if (enableAimPrediction)
                {
                    targetPosition = AimbotPrediction.PredictPosition(FirePos, targetBonePosition, TargetPlayer.Velocity, localPlayer.bullet_speed, localPlayer.bullet_mass, localPlayer.bullet_diam, localPlayer.ballistic_coeff);
                }
                else
                {
                    targetPosition = targetBonePosition;
                }

                Vector2 aimAngle = CalcAngle(FirePos, targetPosition);

                if (aimbotEnabled)
                {
                    if (aimbotHeld)
                    {
                        NormalizeAngle(ref aimAngle);
                        localPlayer.SetRotationFr(aimAngle);
                    }                 
                }
                if(saimbotEnabled)
                {
                    if (saAimbotHeld)
                    {
                        ApplySilentAim(aimAngle);
                    }
                }

                // Reset bone cache and update last execution time
                ResetBoneCache();
                lastBoneReadTime = DateTime.Now;
            }
        }  
        private void ResetBoneCache()
        {
            boneCache = false;
            boneTransforms.Clear();
            BonePointers.Clear();
        }
        private Player GetTargetPlayerOptimized()
        {
            if (!InputManager.IsKeyDown((Keys)aimbotKey) && !InputManager.IsKeyDown((Keys)saAimbotKey))
            {
                return null;  // Prevent execution unless key is pressed
            }

            var localPosition = localPlayer.Position;
            var screenCenter = new Vector2(1920 / 2f, 1080 / 2f);
            var players = this.AllPlayers?.Select(x => x.Value)
                        .Where(x => x.IsActive && x.IsAlive && Vector3.Distance(x.Position, localPlayer.Position) < aimbotMaxDistance)
                        .ToList();
                        
            var validPlayers = this.AllPlayers?.Select(x => x.Value)
                        .Where(x => x.IsActive && x.IsAlive && Vector3.Distance(x.Position, localPlayer.Position) < aimbotMaxDistance)
                        .Select(p =>
                {
                    if (Extensions.WorldToScreen(p.Position, 1920, 1080, out Vector2 screenPos))
                    {
                        float screenDistance = Vector2.Distance(screenPos, screenCenter);
                        float worldDistance = Vector3.Distance(localPosition, p.Position);
                        return new { Player = p, ScreenDistance = screenDistance, WorldDistance = worldDistance };
                    }
                    return null;
                })
                .Where(result => result != null && result.ScreenDistance <= aimbotFOV);

            return aimbotClosest 
                ? validPlayers.OrderBy(result => result.WorldDistance).ThenBy(result => result.ScreenDistance).FirstOrDefault()?.Player 
                : validPlayers.OrderBy(result => result.ScreenDistance).FirstOrDefault()?.Player;
        }
        private static void NormalizeAngle(ref Vector2 angle)
        {
            NormalizeAngle(ref angle.X);
            NormalizeAngle(ref angle.Y);

        }

        private static void NormalizeAngle(ref float angle)
        {
            while (angle > 180.0f) angle -= 360.0f;
            while (angle < -180.0f) angle += 360.0f;
        }
        public static Vector2 CalcAngle(Vector3 from, Vector3 to)
        {
            Vector3 delta = from - to;
            float length = delta.Length();

            return new Vector2(
                RadToDeg((float)-Math.Atan2(delta.X, -delta.Z)),
                RadToDeg((float)Math.Asin(delta.Y / length))
            );
        }

        private static float DegToRad(float degrees)
        {
            return degrees * ((float)Math.PI / 180.0f);
        }

        private static float RadToDeg(float radians)
        {
            return radians * (180.0f / (float)Math.PI);
        }

        private static void ApplySilentAim(Vector2 aimAngle)
        {
            // Read current view angles
            Vector2 viewAngles = Memory.ReadValue<Vector2>(playerManager._movementContext + 0x27C);

            // Normalize delta
            Vector2 delta = aimAngle - viewAngles;
            NormalizeAngle(ref delta);

            // Compute gun angle
            Vector3 gunAngle = new Vector3(
                DegToRad(delta.X) / 1.5f,
                0.0f,
                DegToRad(delta.Y) / 1.5f
            );

            // Write the new gun angles to memory
            Memory.WriteValue(playerManager._proceduralWeaponAnimation + 0x22C, new Vector3(gunAngle.X, -1.0f, gunAngle.Z * -1.0f));
        }

        private List<PlayerBones> bones;
        public void InitializeBonesFromConfig()
        {
            bones = new List<PlayerBones>();

            if (aimbotHead)
                bones.Add(PlayerBones.HumanHead);
            if (aimbotNeck)
                bones.Add(PlayerBones.HumanNeck);
            if (aimbotChest)
                bones.Add(PlayerBones.HumanSpine3);
            if (aimbotPelvis)
                bones.Add(PlayerBones.HumanPelvis);
            if (aimbotRightLeg)
                bones.Add(PlayerBones.HumanRCalf);
            if (aimbotLeftLeg)
                bones.Add(PlayerBones.HumanLCalf);
        }               
        public Vector3 ReadAllBonePositions(Player player)
        {
            if (player == null || player.Name == "???" || !player.IsAlive)
            {
                //Program.Log("Skipping invalid or dead player. Clearing target.");
                TargetPlayer = null; // Clear the target
                return (Vector3.Zero);
            }
            InitializeBonesFromConfig();
            // Reset bone cache
            boneCache = false;

            if (!boneCache)
            {
                boneCache = true;
                boneCounter = bones.Count + 3; // Include Fireport position and both velocities
                boneScatterMap = new ScatterReadMap(boneCounter);

                var baseReadRound = boneScatterMap.AddRound();
                var derefReadRound = boneScatterMap.AddRound();
                // Add scatter read for bones
                for (int i = 0; i < bones.Count; i++)
                {
                    var boneMatrix = Memory.ReadPtrChain(player.PlayerBody, new uint[] { 0x30, 0x30, 0x10 });
                    var basePtr = baseReadRound.AddEntry<MemPointer>(i, 0, boneMatrix, null, 0x20 + ((uint)bones[i] * 0x8));
                    derefReadRound.AddEntry<MemPointer>(i, 1, basePtr, null, 0x10);
                }
                boneScatterMap.Execute();
                // Populate bone transforms
                for (int i = 0; i < bones.Count; i++)
                {
                    if (boneScatterMap.Results[i][1].TryGetResult<MemPointer>(out var bonePointer))
                    {
                        BonePointers.Add(bonePointer);
                        if (!boneTransforms.ContainsKey(bones[i]))
                        {
                            boneTransforms[bones[i]] = new Transform(bonePointer, false);
                        }
                    }
                }
            }
            // Retrieve the target bone position (using the first bone in the list as an example)
            Vector3 targetBonePosition = Vector3.Zero;
            if (bones.Count > 0 && boneTransforms.ContainsKey(bones[0]))
            {
                Vector3 rawBonePosition = boneTransforms[bones[0]].GetPosition();
                targetBonePosition = rawBonePosition;
            }
            // Find the closest bone to the center of the screen
            Vector2 screenCenter = new Vector2(1920 / 2f, 1080 / 2f);
            Vector3 closestBonePosition = Vector3.Zero;
            float closestDistance = float.MaxValue;            
            foreach (var bone in bones)
            {
                if (boneScatterMap.Results[bones.IndexOf(bone)][1].TryGetResult<MemPointer>(out var bonePointer))
                {
                    Transform boneTransform = new Transform(bonePointer);
                    Vector3 bonePosition = boneTransform.GetPosition();

                    if (Extensions.WorldToScreen(bonePosition, 1920, 1080, out Vector2 screenPos))
                    {
                        float distanceToCenter = Vector2.Distance(screenPos, screenCenter);

                        if (distanceToCenter < closestDistance)
                        {
                            closestDistance = distanceToCenter;
                            closestBonePosition = bonePosition;
                        }
                    }
                }
            }
            return (closestBonePosition);
        }
    }    
    public class AimbotPrediction
    {
        public struct DragElement
        {
            public float Mach;
            public float Ballist;
            public DragElement(float mach, float ballist)
            {
                Mach = mach;
                Ballist = ballist;
            }
        }
        private const int DragCoefsCount = 79;
        private const float ConstGravity = 9.81f;
        private static readonly DragElement[] DragCoefs =
        {
            new DragElement(0f, 0.2629f),  new DragElement(0.05f, 0.2558f), new DragElement(0.1f, 0.2487f),
            new DragElement(0.15f, 0.2413f), new DragElement(0.2f, 0.2344f), new DragElement(0.25f, 0.2278f),
            new DragElement(0.3f, 0.2214f), new DragElement(0.35f, 0.2155f), new DragElement(0.4f, 0.2104f),
            new DragElement(0.45f, 0.2061f), new DragElement(0.5f, 0.2032f), new DragElement(0.55f, 0.202f),
            // Add remaining values as per the original array...
            new DragElement(5.0f, 0.4988f)
        };
        public static float CalculateG1DragCoefficient(float velocity)
        {
            int coefIdx = (int)Math.Floor(velocity / 343f / 0.05f);
            // Ensure index is within valid bounds
            if (coefIdx < 0) return DragCoefs[0].Ballist;
            if (coefIdx >= DragCoefs.Length) return DragCoefs[^1].Ballist;
            float prevDrag = DragCoefs[coefIdx - 1].Mach * 343f;
            float curDrag = DragCoefs[coefIdx].Mach * 343f;
            float ballist = DragCoefs[coefIdx - 1].Ballist;
            return ((DragCoefs[coefIdx].Ballist - ballist) / (curDrag - prevDrag)) * (velocity - prevDrag) + ballist;
        }
        public static float GetBulletTimeForDistance(float distance, float bulletSpeed, float bulletMassGram, float bulletDiameter, float ballisticCoefficient)
        {
            Vector3 zeroPosition = Vector3.Zero;
            Vector3 zeroVelocity = new Vector3(bulletSpeed, 0f, 0f);
            float bulletMassKg = bulletMassGram / 1000f;
            float bulletDiamM = bulletDiameter / 1000f;
            float bulletArea = bulletDiamM * bulletDiamM * (float)Math.PI / 4f;
            float elapsedTime = 0.01f;
            Vector3 prevVelocity = zeroVelocity;
            Vector3 prevPosition = zeroPosition;
            while (elapsedTime < 10f)
            {
                float fix = bulletMassKg * 0.0014223f / (bulletDiamM * bulletDiamM * ballisticCoefficient);
                float dragCoeff = CalculateG1DragCoefficient(prevVelocity.Length()) * fix;
                Vector3 acceleration = new Vector3(
                    -dragCoeff * 1.2f * bulletArea * prevVelocity.LengthSquared() / (2f * bulletMassKg),
                    ConstGravity,
                    0f
                );
                Vector3 nextPosition = prevPosition + prevVelocity * 0.01f + 0.00005f * acceleration;
                Vector3 nextVelocity = prevVelocity + acceleration * 0.01f;
                prevVelocity = nextVelocity;
                prevPosition = nextPosition;
                elapsedTime += 0.01f;
                if (Vector3.Distance(prevPosition, zeroPosition) >= distance)
                    break;
            }
            return elapsedTime;
        }
        public static Vector3 PredictPosition(Vector3 fireport, Vector3 targetPosition, Vector3 targetVelocity, float bulletSpeed, float bulletMass, float bulletDiameter, float ballisticCoefficient)
        {
            float distance = Vector3.Distance(fireport, targetPosition);
            float bulletTime = GetBulletTimeForDistance(distance, bulletSpeed, bulletMass, bulletDiameter, ballisticCoefficient);
            float verticalDisplacement = 0.5f * ConstGravity * bulletTime * bulletTime;
            targetPosition.Y += verticalDisplacement;
            Vector3 predictionDisplacement = targetVelocity * bulletTime;
            targetPosition += predictionDisplacement;
            return targetPosition;
        }       
    }   
}