using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using Offsets;
using eft_dma_radar.Source.Tarkov;

namespace eft_dma_radar
{
    public class Player
    {
        private static readonly Dictionary<string, int> _groups = new(StringComparer.OrdinalIgnoreCase);
        private GearManager _gearManager;
        private readonly ConcurrentDictionary<PlayerBones, Bone> _bones = new();

        #region BonePosition
        private Vector3 GetBonePosition(PlayerBones bone) =>
            this.Bones.TryGetValue(bone, out var boneValue) ? boneValue.Position : Vector3.Zero;

        public Vector3 BasePosition => GetBonePosition(PlayerBones.HumanBase);
        public Vector3 HeadPosition => GetBonePosition(PlayerBones.HumanHead);
        public Vector3 Spine3Position => GetBonePosition(PlayerBones.HumanSpine3);
        public Vector3 LPalmPosition => GetBonePosition(PlayerBones.HumanLPalm);
        public Vector3 RPalmPosition => GetBonePosition(PlayerBones.HumanRPalm);
        public Vector3 PelvisPosition => GetBonePosition(PlayerBones.HumanPelvis);
        public Vector3 LFootPosition => GetBonePosition(PlayerBones.HumanLFoot);
        public Vector3 RFootPosition => GetBonePosition(PlayerBones.HumanRFoot);
        public Vector3 LForearm1Position => GetBonePosition(PlayerBones.HumanLForearm1);
        public Vector3 RForearm1Position => GetBonePosition(PlayerBones.HumanRForearm1);
        public Vector3 LCalfPosition => GetBonePosition(PlayerBones.HumanLCalf);
        public Vector3 RCalfPosition => GetBonePosition(PlayerBones.HumanRCalf);
        #endregion

        #region PlayerProperties
        public bool IsPMC { get; set; }
        public bool IsLocalPlayer { get; set; }
        public volatile bool IsAlive = true;
        public volatile bool IsActive = true;
        public string AccountID { get; set; }
        public string ProfileID { get; set; }
        public string Name { get; set; } = "";
        public int Level { get; private set; } = 0;
        public float KDA { get; private set; } = -1f;
        public float Hours { get; private set; } = -1f;
        public int GroupID { get; set; } = -1;
        public PlayerType Type { get; set; }
        public float bullet_speed { get; set; }
        public float ballistic_coeff { get; set; }
        public float bullet_mass { get; set; }
        public float bullet_diam { get; set; }
        public float bullet_velocity { get; set; }
        public ulong CharacterController { get; set; }
        public ulong FireportPtr { get; set; }
        public Vector3 fireportPosition { get; set; }
        public int Health { get; private set; } = -1;
        public ulong HealthController { get; set; }
        public ulong InventoryController { get; set; }
        public ulong InventorySlots { get; set; }
        public ulong PlayerBody { get; set; }

        public Vector3 Position => this.Bones.TryGetValue(PlayerBones.HumanHead, out var bone) ? bone.Position : Vector3.Zero;
        public Vector2 ZoomedPosition { get; set; } = new();
        public Vector2 Rotation { get; private set; } = new Vector2(0, 0);
        public List<GearManager.Gear> Gear => this._gearManager?.GearItems;
        public GearManager GearManager => this._gearManager;
        public bool LastUpdate { get; set; } = false;
        public int ErrorCount { get; set; } = 0;
        public bool isOfflinePlayer { get; set; } = false;
        public int PlayerSide { get; set; }
        public int PlayerRole { get; set; }
        public bool HasRequiredGear { get; set; } = false;
        public Vector3 Velocity { get; private set; } = Vector3.Zero;
        public ConcurrentDictionary<PlayerBones, Bone> Bones => this._bones;

        public static List<PlayerBones> RequiredBones { get; } = new List<PlayerBones>
        {
            PlayerBones.HumanHead,
            PlayerBones.HumanNeck,
            PlayerBones.HumanSpine3,
            PlayerBones.HumanLPalm,
            PlayerBones.HumanRPalm,
            PlayerBones.HumanPelvis,
            PlayerBones.HumanLFoot,
            PlayerBones.HumanRFoot,
            PlayerBones.HumanLForearm1,
            PlayerBones.HumanRForearm1,
            PlayerBones.HumanLCalf,
            PlayerBones.HumanRCalf
        };

        private static Watchlist _watchlistManager => Program.Config.Watchlist;

        public bool IsHuman => this.Type is PlayerType.LocalPlayer or PlayerType.Teammate or PlayerType.PMC or PlayerType.Special or PlayerType.PlayerScav or PlayerType.BEAR or PlayerType.USEC;
        public bool IsHumanActive => this.IsHuman && IsActive && IsAlive;
        public bool IsHumanHostile => this.Type is PlayerType.PMC or PlayerType.Special or PlayerType.PlayerScav or PlayerType.BEAR or PlayerType.USEC;
        public bool IsHumanHostileActive => this.Type is PlayerType.BEAR or PlayerType.USEC or PlayerType.Special or PlayerType.PlayerScav && this.IsActive && this.IsAlive;
        public bool IsBossRaider => this.Type is PlayerType.Raider or PlayerType.BossFollower or PlayerType.BossGuard or PlayerType.Rogue or PlayerType.Cultist or PlayerType.Boss;
        public bool IsRogueRaider => this.Type is PlayerType.Raider or PlayerType.BossFollower or PlayerType.BossGuard or PlayerType.Rogue or PlayerType.Cultist;
        public bool IsEventAI => this.Type is PlayerType.FollowerOfMorana or PlayerType.Zombie;
        public bool IsHostileActive => this.Type is PlayerType.PMC or PlayerType.BEAR or PlayerType.USEC or PlayerType.Special or PlayerType.PlayerScav or PlayerType.Scav or PlayerType.Raider or PlayerType.BossFollower or PlayerType.BossGuard or PlayerType.Rogue or PlayerType.OfflineScav or PlayerType.Cultist or PlayerType.Zombie or PlayerType.Boss && this.IsActive && this.IsAlive;
        public bool IsFriendlyActive => (this.Type is PlayerType.LocalPlayer or PlayerType.Teammate) && this.IsActive && this.IsAlive;
        public bool IsZombie => this.Type is PlayerType.Zombie;
        public bool HasExfild => !this.IsActive && this.IsAlive;
        public int Value => this._gearManager?.Value ?? 0;
        public ulong Base { get; }
        public ulong Profile { get; }
        public ulong Info { get; set; }
        public ulong[] HealthEntries { get; set; }
        public ulong MovementContext { get; set; }
        public ulong CorpsePtr => this.Base + Offsets.Player.Corpse;
        public int MarkedDeadCount { get; set; } = 0;
        public string Tag { get; set; } = string.Empty;

        public string HealthStatus => this.Health switch
        {
            100 => "Healthy",
            >= 75 => "Moderate",
            >= 45 => "Poor",
            >= 20 => "Critical",
            _ => "n/a"
        };

        public bool HasThermal => _gearManager.HasThermal;
        public bool HasNVG => _gearManager.HasNVG;

        public GearManager.Gear ItemInHands { get; set; }
        #endregion

        #region Constructor
        public Player(ulong playerBase, ulong playerProfile, string profileID, Vector3? pos = null, string baseClassName = null)
        {
            if (string.IsNullOrEmpty(baseClassName))
                throw new Exception("BaseClass is not set!");

            var isOfflinePlayer = string.Equals(baseClassName, "ClientPlayer") || string.Equals(baseClassName, "LocalPlayer") || string.Equals(baseClassName, "HideoutPlayer");
            var isOnlinePlayer = string.Equals(baseClassName, "ObservedPlayerView");

            if (!isOfflinePlayer && !isOnlinePlayer)
                throw new Exception("Player is not of type OfflinePlayer or OnlinePlayer");

            Debug.WriteLine("Player Constructor: Initialization started.");

            this.Base = playerBase;
            this.Profile = playerProfile;
            this.ProfileID = profileID;

            var scatterReadMap = new ScatterReadMap(1);

            if (isOfflinePlayer)
            {
                this.SetupOfflineScatterReads(scatterReadMap);
                this.ProcessOfflinePlayerScatterReadResults(scatterReadMap);
            }
            else if (isOnlinePlayer)
            {
                this.Info = playerBase;
                this.SetupOnlineScatterReads(scatterReadMap);
                this.ProcessOnlinePlayerScatterReadResults(scatterReadMap);
            }
        }
        #endregion

        #region Aimbot
        public bool SetAmmo()
        {
            try
            {
                if (!this.IsLocalPlayer || !this.IsAlive)
                    return false;

                var ammo_template = Memory.ReadPtrChain(this.Base, [Offsets.Player.HandsController, 0x68, 0x40, 0x198]);
                if (ammo_template != 0)
                {
                    this.bullet_speed = Memory.ReadValue<float>(ammo_template + 0x1BC);
                    this.ballistic_coeff = Memory.ReadValue<float>(ammo_template + 0x1D0);
                    this.bullet_mass = Memory.ReadValue<float>(ammo_template + 0x258);
                    this.bullet_diam = Memory.ReadValue<float>(ammo_template + 0x25C);
                    this.bullet_velocity = Memory.ReadValue<float>(ammo_template + 0x1BC);
                }
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        public void SetRotationFr(Vector2 brainrot)
        {
            if (!this.IsLocalPlayer || !this.IsAlive || this.MovementContext == 0)
                return;

            Memory.WriteValue<Vector2>(this.MovementContext + Offsets.MovementContext._Rotation, brainrot);
        }

        public Vector2 GetRotationFr()
        {
            if (!this.IsLocalPlayer || !this.IsAlive || this.MovementContext == 0)
                return new Vector2();

            return Memory.ReadValue<Vector2>(this.isOfflinePlayer ? this.MovementContext + Offsets.MovementContext.Rotation : this.MovementContext + Offsets.ObservedPlayerMovementContext.Rotation);
        }
        #endregion

        #region Setters
        public bool SetHealth(int eTagStatus)
        {
            try
            {
                this.Health = eTagStatus switch
                {
                    1024 => 100,
                    2048 => 75,
                    4096 => 45,
                    8192 => 20,
                    _ => 100,
                };
                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"ERROR getting Player '{this.Name}' Health: {ex}");
                return false;
            }
        }

        public bool SetRotation(object obj)
        {
            try
            {
                if (obj is not Vector2 rotation)
                    throw new ArgumentException("Rotation data must be of type Vector2.", nameof(obj));

                rotation.X = (rotation.X - 90 + 360) % 360;
                rotation.Y = (rotation.Y) % 360;

                this.Rotation = rotation;
                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"ERROR getting Player '{this.Name}' Rotation: {ex}");
                return false;
            }
        }

        public bool SetVelocity(object obj)
        {
            try
            {
                if (obj is not Vector3 velocity)
                    throw new ArgumentException("Velocity data must be of type Vector3.", nameof(obj));

                this.Velocity = velocity;
                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"ERROR getting Player '{this.Name}' Velocity: {ex}");
                return false;
            }
        }

        public bool SetFireArmPos()
        {
            try
            {
                if (!this.IsLocalPlayer)
                {
                    Program.Log($"Skipping firearm position update for non-local player '{this.Name}'");
                    return false;
                }

                if (this.FireportPtr == 0)
                    throw new InvalidOperationException($"FireportPosition is invalid for Player '{this.Name}'.");

                ulong handsContainer = Memory.ReadPtrChain(FireportPtr, new uint[] { Fireport.To_TransfromInternal[0], Fireport.To_TransfromInternal[1] });
                Transform fireportTransform = new Transform(handsContainer);
                fireportPosition = fireportTransform.GetPosition();

                if (fireportPosition == Vector3.Zero)
                {
                    Program.Log($"ERROR: Fireport position is zero for Player '{this.Name}'");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Program.Log($"ERROR setting FireportPosition for Player '{this.Name}': {ex}");
                return false;
            }
        }

        public void UpdateItemInHands()
        {
            this.ItemInHands = this.GearManager.ActiveWeapon;
        }

        public void CheckForRequiredGear()
        {
            if (this.Gear.Count < 1)
                return;

            var found = false;
            var loot = Memory.Loot;
            var requiredQuestItems = QuestManager.RequiredItems;

            foreach (var gearItem in this.Gear)
            {
                var parentItem = gearItem.Item.ID;

                if (requiredQuestItems.Contains(parentItem) ||
                    gearItem.Item.Loot.Any(x => requiredQuestItems.Contains(x.ID)) ||
                    (loot is not null && loot.RequiredFilterItems is not null && (loot.RequiredFilterItems.ContainsKey(parentItem) ||
                                      gearItem.Item.Loot.Any(x => loot.RequiredFilterItems.ContainsKey(x.ID))))
                )
                {
                    found = true;
                    break;
                }
            }

            this.HasRequiredGear = found;
        }
        #endregion

        #region Methods
        private PlayerType GetOnlinePlayerType()
        {
            var isAI = this.AccountID == "0";

            if (!isAI)
            {
                return this.PlayerSide switch
                {
                    1 => PlayerType.USEC,
                    2 => PlayerType.BEAR,
                    _ => PlayerType.PlayerScav,
                };
            }
            else
            {
                if (string.IsNullOrEmpty(this.Name))
                    return PlayerType.Scav;

                var inFaction = Program.AIFactionManager.IsInFaction(this.Name, out var playerType);

                if (!inFaction && Memory.IsPvEMode)
                {
                    var dogtagSlot = this.Gear.FirstOrDefault(x => x.Slot.Key == "Dogtag");

                    if (dogtagSlot.Item is not null)
                        playerType = (dogtagSlot.Item.Short == "BEAR" ? PlayerType.BEAR : PlayerType.USEC);
                }
                else if (!inFaction && this.Name.Equals("???", StringComparison.OrdinalIgnoreCase))
                {
                    playerType = PlayerType.Zombie;
                }

                return playerType;
            }
        }

        private PlayerType GetOfflinePlayerType(bool isAI)
        {
            if (!isAI)
            {
                return PlayerType.LocalPlayer;
            }
            else
            {
                if (this.Name.Contains("(BTR)"))
                {
                    return PlayerType.Boss;
                }
                else if (this.PlayerRole == 51 || this.PlayerRole == 52)
                {
                    return (this.PlayerRole == 51 ? PlayerType.BEAR : PlayerType.USEC);
                }
                else if (Program.AIFactionManager.IsInFaction(this.Name, out var playerType))
                {
                    return playerType;
                }
                else if (this.Name == "???")
                {
                    return PlayerType.Zombie;
                }
                else
                {
                    return PlayerType.Scav; // default to scav
                }
            }
        }

        private void SetupOfflineScatterReads(ScatterReadMap scatterReadMap)
        {
            var round1 = scatterReadMap.AddRound();
            var round2 = scatterReadMap.AddRound();
            var round3 = scatterReadMap.AddRound();
            var round4 = scatterReadMap.AddRound();
            var round5 = scatterReadMap.AddRound();
            var round6 = scatterReadMap.AddRound();

            var info = round1.AddEntry<ulong>(0, 0, this.Profile, null, Offsets.Profile.PlayerInfo);
            var inventoryController = round1.AddEntry<ulong>(0, 1, this.Base, null, Offsets.Player.InventoryController);
            var playerBody = round1.AddEntry<ulong>(0, 2, this.Base, null, Offsets.Player.PlayerBody);
            var movementContext = round1.AddEntry<ulong>(0, 3, this.Base, null, Offsets.Player.MovementContext);
            var healthController = round1.AddEntry<ulong>(0, 4, this.Base, null, Offsets.Player.HealthController);

            var name = round2.AddEntry<ulong>(0, 5, info, null, Offsets.PlayerInfo.Nickname);
            var inventory = round2.AddEntry<ulong>(0, 6, inventoryController, null, Offsets.InventoryController.Inventory);
            var registrationDate = round2.AddEntry<int>(0, 7, info, null, Offsets.PlayerInfo.RegistrationDate);
            var groupID = round2.AddEntry<ulong>(0, 8, info, null, Offsets.PlayerInfo.GroupId);
            var botSettings = round2.AddEntry<ulong>(0, 9, info, null, Offsets.PlayerInfo.Settings);

            var equipment = round3.AddEntry<ulong>(0, 10, inventory, null, Offsets.Inventory.Equipment);
            var role = round3.AddEntry<int>(0, 11, botSettings, null, Offsets.PlayerSettings.Role);

            var inventorySlots = round4.AddEntry<ulong>(0, 12, equipment, null, Offsets.Equipment.Slots);

            var characterController = round1.AddEntry<ulong>(0, 13, this.Base, null, Offsets.Player.CharacterController);

            // Add Velocity scatter read from CharacterController
            var velocity = round2.AddEntry<Vector3>(0, 14, characterController, null, Offsets.CharacterController.Velocity);
            var pwd = round2.AddEntry<ulong>(0, 21, this.Base, null, Offsets.Player.ProceduralWeaponAnimation);
            var firearmContoller = round3.AddEntry<ulong>(0, 15, pwd, null, Offsets.ProceduralWeaponAnimation.FirearmController);
            var firePortptr = round4.AddEntry<ulong>(0, 16, firearmContoller, null, Offsets.FirearmController.Fireport);
            scatterReadMap.Execute();
        }

        private void ProcessOfflinePlayerScatterReadResults(ScatterReadMap scatterReadMap)
        {
            if (!scatterReadMap.Results[0][0].TryGetResult<ulong>(out var info))
                return;
            if (!scatterReadMap.Results[0][1].TryGetResult<ulong>(out var inventoryController))
                return;
            if (!scatterReadMap.Results[0][2].TryGetResult<ulong>(out var playerBody))
                return;
            if (!scatterReadMap.Results[0][3].TryGetResult<ulong>(out var movementContext))
                return;
            if (!scatterReadMap.Results[0][4].TryGetResult<ulong>(out var healthController))
                return;
            if (!scatterReadMap.Results[0][5].TryGetResult<ulong>(out var name))
                return;
            if (!scatterReadMap.Results[0][8].TryGetResult<ulong>(out var groupID))
                return;
            if (!scatterReadMap.Results[0][11].TryGetResult<int>(out var role))
                return;
            if (!scatterReadMap.Results[0][12].TryGetResult<ulong>(out var inventorySlots))
                return;
            if (!scatterReadMap.Results[0][13].TryGetResult<ulong>(out var characterController))
                return;
            if (!scatterReadMap.Results[0][16].TryGetResult<ulong>(out var firePortptr))
                return;

            this.Info = info;
            this.PlayerRole = role;
            this.HealthController = healthController;
            this.CharacterController = characterController;
            this.FireportPtr = firePortptr;
            if (scatterReadMap.Results[0][14].TryGetResult<Vector3>(out var velocity))
            {
                this.Velocity = velocity;
                Program.Log($"Got Velocity Info Offline '{velocity.X}' '{velocity.Y}' '{velocity.Z}'");
            }
            else
            {
                this.Velocity = Vector3.Zero;
                Program.Log($"Couldn't get Velocity Info '0' '0' '0'");
            }
            this.InitializePlayerProperties(movementContext, inventoryController, inventorySlots, playerBody, name, groupID);

            if (scatterReadMap.Results[0][7].TryGetResult<int>(out var registrationDate))
            {
                var isAI = registrationDate == 0;

                this.IsLocalPlayer = !isAI;
                this.isOfflinePlayer = true;
                this.Type = this.GetOfflinePlayerType(isAI);
                this.IsPMC = (this.Type == PlayerType.BEAR || this.Type == PlayerType.USEC || !isAI);

                this.FinishAlloc();
            }
        }

        private void SetupOnlineScatterReads(ScatterReadMap scatterReadMap)
        {
            var round1 = scatterReadMap.AddRound();
            var round2 = scatterReadMap.AddRound();
            var round3 = scatterReadMap.AddRound();
            var round4 = scatterReadMap.AddRound();
            var round5 = scatterReadMap.AddRound();
            var round6 = scatterReadMap.AddRound();

            var movementContextPtr1 = round1.AddEntry<ulong>(0, 0, this.Info, null, Offsets.ObservedPlayerView.To_MovementContext[0]);
            var inventoryControllerPtr1 = round1.AddEntry<ulong>(0, 1, this.Info, null, Offsets.ObservedPlayerView.To_InventoryController[0]);
            var healthControllerPtr1 = round1.AddEntry<ulong>(0, 2, this.Info, null, Offsets.ObservedPlayerView.To_HealthController[0]);
            var accountID = round1.AddEntry<ulong>(0, 3, this.Info, null, Offsets.ObservedPlayerView.AccountID);
            var playerSide = round1.AddEntry<int>(0, 4, this.Info, null, Offsets.ObservedPlayerView.PlayerSide);
            var groupID = round1.AddEntry<ulong>(0, 5, this.Info, null, Offsets.ObservedPlayerView.GroupID);
            var playerBody = round1.AddEntry<ulong>(0, 6, this.Info, null, Offsets.ObservedPlayerView.PlayerBody);
            var memberCategory = round1.AddEntry<int>(0, 7, this.Info, null, Offsets.PlayerInfo.MemberCategory);
            var voiceStr = round1.AddEntry<ulong>(0, 8, this.Info, null, Offsets.ObservedPlayerView.VoiceName);

            var movementContextPtr2 = round2.AddEntry<ulong>(0, 9, movementContextPtr1, null, Offsets.ObservedPlayerView.To_MovementContext[1]);
            var inventoryController = round2.AddEntry<ulong>(0, 10, inventoryControllerPtr1, null, Offsets.ObservedPlayerView.To_InventoryController[1]);
            var healthController = round2.AddEntry<ulong>(0, 11, healthControllerPtr1, null, Offsets.ObservedPlayerView.To_HealthController[1]);

            var movementContext = round3.AddEntry<ulong>(0, 12, movementContextPtr2, null, Offsets.ObservedPlayerView.To_MovementContext[2]);
            var inventory = round3.AddEntry<ulong>(0, 13, inventoryController, null, Offsets.InventoryController.Inventory);

            var equipment = round4.AddEntry<ulong>(0, 14, inventory, null, Offsets.Inventory.Equipment);

            var inventorySlots = round5.AddEntry<ulong>(0, 15, equipment, null, Offsets.Equipment.Slots);

            var velocity = round4.AddEntry<Vector3>(0, 16, movementContext, null, 0x10C);
            scatterReadMap.Execute();
        }

        private void ProcessOnlinePlayerScatterReadResults(ScatterReadMap scatterReadMap)
        {
            if (!scatterReadMap.Results[0][3].TryGetResult<ulong>(out var accountID))
                return;
            if (!scatterReadMap.Results[0][4].TryGetResult<int>(out var playerSide))
                return;
            if (!scatterReadMap.Results[0][5].TryGetResult<ulong>(out var groupID))
                return;
            if (!scatterReadMap.Results[0][6].TryGetResult<ulong>(out var playerBody))
                return;
            if (!scatterReadMap.Results[0][7].TryGetResult<int>(out var memberCategory))
                return;
            if (!scatterReadMap.Results[0][8].TryGetResult<ulong>(out var voiceName))
                return;
            if (!scatterReadMap.Results[0][10].TryGetResult<ulong>(out var inventoryController))
                return;
            if (!scatterReadMap.Results[0][11].TryGetResult<ulong>(out var healthController))
                return;
            if (!scatterReadMap.Results[0][12].TryGetResult<ulong>(out var movementContext))
                return;
            if (!scatterReadMap.Results[0][15].TryGetResult<ulong>(out var inventorySlots))
                return;

            this.InitializePlayerProperties(movementContext, inventoryController, inventorySlots, playerBody, 0, groupID, playerSide);

            this.IsLocalPlayer = false;
            this.HealthController = healthController;
            this.AccountID = Memory.ReadUnityString(accountID);

            this.Type = this.GetOnlinePlayerType();
            this.IsPMC = (this.Type == PlayerType.BEAR || this.Type == PlayerType.USEC);
            if (scatterReadMap.Results[0][16].TryGetResult<Vector3>(out var velocity))
            {
                this.Velocity = velocity;
                Program.Log($"Got Velocity Info Online '{velocity.X}' '{velocity.Y}' '{velocity.Z}'");
            }
            else
            {
                this.Velocity = Vector3.Zero;
                Program.Log($"Couldn't get Online Velocity Info '0' '0' '0'");
            }
            if (this.IsHuman)
            {
                this.Name = "Human";
                Task.Run(async () =>
                {
                    try
                    {
                        var stats = await PlayerProfileAPI.GetPlayerStatsAsync(this.AccountID);
                        this.Name = stats.Nickname;
                        this.Level = stats.Level;
                        this.KDA = stats.KDRatio;
                        this.Hours = stats.HoursPlayed;

                        Program.Log($"Updated Player stats for '{this.Name}'");
                    }
                    catch { }
                    finally
                    {
                        this.FinishAlloc();
                    }
                });
            }
            else // npc
            {
                try
                {
                    this.Name = Memory.ReadUnityString(voiceName);
                    this.CleanAIName();
                }
                catch
                {
                    this.Name = "AI";
                }

                this.FinishAlloc();
            }
        }

        private void InitializePlayerProperties(ulong movementContext, ulong inventoryController, ulong inventorySlots, ulong playerBody, ulong name, ulong groupID, int playerSide = 0)
        {
            this.MovementContext = movementContext;
            this.InventoryController = inventoryController;
            this.InventorySlots = inventorySlots;
            this._gearManager = new GearManager(this.InventorySlots);
            this.PlayerBody = playerBody;

            if (name > 0)
            {
                this.Name = Memory.ReadUnityString(name);
                this.Name = Helpers.TransliterateCyrillic(this.Name);
            }

            this.PlayerSide = playerSide;

            if (groupID != 0)
            {
                var group = Memory.ReadUnityString(groupID);
                _groups.TryAdd(group, _groups.Count);
                this.GroupID = _groups[group];
            }
            else
            {
                this.GroupID = -1;
            }

            this.SetupBones();
        }

        private ulong GetBoneMatrix()
        {
            return Memory.ReadPtrChain(this.PlayerBody, new uint[] { 0x30, 0x30, 0x10 });
        }

        private ulong GetBonePointer(ulong boneMatrix, PlayerBones bone)
        {
            var boneOffset = 0x20 + ((uint)bone * 0x8);
            return Memory.ReadPtrChain(boneMatrix, new uint[] { boneOffset, 0x10 });
        }

        private void ProcessBone(ulong boneMatrix, PlayerBones bone, bool isRefresh = false)
        {
            var bonePointer = GetBonePointer(boneMatrix, bone);
            if (bonePointer == 0) return;

            if (isRefresh && this._bones.TryGetValue(bone, out var boneTransform))
            {
                boneTransform.UpdateTransform(bonePointer);
            }
            else
            {
                this._bones.TryAdd(bone, new Bone(bonePointer));
            }
        }

        private void ProcessBones(bool isRefresh, string operation)
        {
            try
            {
                var boneMatrix = GetBoneMatrix();
                if (boneMatrix == 0) return;

                foreach (var bone in Player.RequiredBones)
                {
                    ProcessBone(boneMatrix, bone, isRefresh);
                }
            }
            catch (Exception ex)
            {
                Program.Log($"ERROR {operation} bones for Player '{this.Name}': {ex}");
            }
        }

        private void SetupBones()
        {
            ProcessBones(false, "setting up");
        }

        public void RefreshBoneTransforms()
        {
            ProcessBones(true, "refreshing");
        }

        private void FinishAlloc()
        {
            if (this.IsHumanHostile)
                this.RefreshWatchlistStatus();

            if (this.Type == PlayerType.Zombie)
                this.Name = "Zombie";
        }

        public async void RefreshWatchlistStatus()
        {
            var isOnWatchlist = _watchlistManager.IsOnWatchlist(this.AccountID, out Watchlist.Entry entry);
            var isSpecialPlayer = this.Type == PlayerType.Special;

            if ((!isSpecialPlayer || isSpecialPlayer) && isOnWatchlist)
            {
                var isLive = false;

                if (entry.IsStreamer)
                {
                    isLive = await Watchlist.IsLive(entry);

                    if (isLive)
                        this.Name += " (LIVE)";
                }

                if (!isLive && this.Name.Contains("(LIVE)"))
                {
                    this.Name = this.Name.Substring(0, this.Name.IndexOf("(LIVE)") - 1);
                }

                if (!string.IsNullOrEmpty(entry.Tag))
                {
                    this.Tag = entry.Tag;
                    this.Type = PlayerType.Special;
                }
            }
            else if (isSpecialPlayer && !isOnWatchlist)
            {
                this.Tag = "";
                this.Type = this.isOfflinePlayer ? this.GetOfflinePlayerType(false) : this.GetOnlinePlayerType();
            }
        }

        public static void Reset()
        {
            _groups.Clear();
        }

        public void CleanAIName()
        {
            var cleanName = Regex.Replace(this.Name, "^Boss[_]?", "", RegexOptions.IgnoreCase);
            cleanName = cleanName.Replace("_", "");
            cleanName = Regex.Replace(cleanName, @"\d", "");

            switch (cleanName)
            {
                case "BigPipe": cleanName = "Big Pipe"; break;
                case "BirdEye": cleanName = "Birdeye"; break;
                case "Usec": cleanName = "Rogue"; break;
                case "Bully": cleanName = "Reshala"; break;
                case "Sturman": cleanName = "Shturman"; break;
                case "Gluhar": cleanName = "Glukhar"; break;
                case "SectantWarrior": cleanName = "Cultist"; break;
                case "SectantPriest" when this.Gear.Any(g => g.Item.Short == "Zryachiy"):
                    cleanName = "Zryachiy";
                    break;
                case "Scav" when this.Gear.Count == 1 && this.Gear.First().Item.Short == "AVS":
                    cleanName = "BTR";
                    break;
            }

            this.Name = cleanName;

            this.Type = this.GetOnlinePlayerType();
        }
        #endregion
    }
}