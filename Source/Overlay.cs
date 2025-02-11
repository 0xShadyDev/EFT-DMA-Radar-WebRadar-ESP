﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D9;
using Color = System.Drawing.Color;
using Vector3 = System.Numerics.Vector3;



namespace eft_dma_radar
{
    public partial class Overlay : Form
    {
        // Constants
        private const int TargetFrameRate = 144;
        private const int TargetFrameTime = 1000 / TargetFrameRate;

        // D3D9 Objects
        private Device _device;
        private Sprite _sprite;
        private SharpDX.Direct3D9.Font _font;
        private Thread _directXThread;
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;
        private bool _isRunning;

        // Game State
        private bool _isInGame;
        private Player _localPlayer;
        private ReadOnlyDictionary<string, Player> _allPlayers;
        private LootManager _loot;
        private List<Exfil> _exfils;
        private List<Grenade> _grenades;
        private List<Tripwire> _tripwires;
        private List<QuestItem> _questItems = new();

        // Config Settings
        private Config _config { get => Program.Config; }    
        
        // Loot ESP Settings
        private bool _isLootItemOn = true;
        private float _lootItemLimit = 250f;
        private bool _isLootContainerOn = true;
        private float _lootContainerLimit = 50f;
        private bool _isLootCorpseOn = true;
        private float _lootCorpseLimit = 80f;
        private bool _isQuestItemOn = true;
        private float _questItemLimit = 150f;

        private List<(string, int, int, Color)> _textBatch = new();
        private List<(int, int, int, int, Color)> _lineBatch = new();

        public struct Vertex
        {
            public SharpDX.Vector3 Position;
            public SharpDX.ColorBGRA Color;

            public static readonly VertexFormat Format = VertexFormat.Position | VertexFormat.Diffuse;
            public static readonly int Stride = Utilities.SizeOf<Vertex>();
        }

        public Overlay(System.Drawing.Rectangle bounds)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new System.Drawing.Point(bounds.Left, bounds.Top);
            Size = new Size(bounds.Width, bounds.Height);
            InitializeComponent();
            InitializeStyles();
        }

        private void InitializeStyles()
        {
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.Opaque |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true
            );
        }

        private void LoadOverlay(object sender, EventArgs e)
        {
            DisposeOverlayResources();
            InitializeDirectXResources();

            _cancellationTokenSource = new CancellationTokenSource();
            _cancellationToken = _cancellationTokenSource.Token;
            _isRunning = true;
            TopMost = true;

            _directXThread = new Thread(DirectXThread)
            {
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };
            _directXThread.Start();
        }

        private void DisposeOverlayResources()
        {
            if (_directXThread != null && _directXThread.IsAlive)
            {
                _isRunning = false;
                _cancellationTokenSource?.Cancel();
                _directXThread.Join();
            }

            _device?.Dispose();
            _sprite?.Dispose();
            _font?.Dispose();

            _device = null;
            _sprite = null;
            _font = null;
            _cancellationTokenSource = null;
            _directXThread = null;
        }

        private void InitializeDirectXResources()
        {
            PresentParameters presentParams = new PresentParameters
            {
                Windowed = true,
                SwapEffect = SwapEffect.Discard,
                DeviceWindowHandle = Handle,
                PresentationInterval = PresentInterval.Immediate
            };

            _device = new Device(new Direct3D(), 0, DeviceType.Hardware, Handle, CreateFlags.HardwareVertexProcessing, presentParams);
            _sprite = new Sprite(_device);

            var fontDescription = new SharpDX.Direct3D9.FontDescription
            {
                FaceName = "Tarkov-Regular",
                Height = 20,
                Weight = SharpDX.Direct3D9.FontWeight.Bold,
                MipLevels = 1,
                Quality = SharpDX.Direct3D9.FontQuality.ClearTypeNatural
            };

            _font = new SharpDX.Direct3D9.Font(_device, fontDescription);
        }

        private void DirectXThread()
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while (_isRunning && !_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    stopwatch.Restart();

                    _device.Clear(ClearFlags.Target, new SharpDX.Color(0, 0, 0, 0), 1.0f, 0);
                    _device.BeginScene();

                    _sprite.Begin(SpriteFlags.AlphaBlend);

                    UpdateGameState();
                    RenderOverlay();

                    _sprite.End();
                    _device.EndScene();
                    _device.Present();

                    var frameTime = stopwatch.ElapsedMilliseconds;
                    var sleepTime = TargetFrameTime - frameTime;
                    if (sleepTime > 0)
                    {
                        Thread.Sleep((int)sleepTime);
                    }
                }
                catch (SharpDXException ex)
                {
                    Console.WriteLine(ex);
                    SafeEndDraw();
                }
            }
        }

        private void UpdateGameState()
        {
            bool wasInGame = _isInGame;
            _isInGame = Memory.InGame;
            _localPlayer = Memory.Players?.FirstOrDefault(x => x.Value.Type is PlayerType.LocalPlayer).Value;
            _allPlayers = Memory.Players;
            _loot = Memory.Loot;
            _exfils = Memory.Exfils;
            _grenades = Memory.Grenades;
            _tripwires = Memory.Tripwires;

            // Detect match end/start
            if (wasInGame && !_isInGame)
            {
                OnMatchEnd();
            }
            else if (!wasInGame && _isInGame)
            {
                OnMatchStart();
            }
        }

        private void OnMatchEnd()
        {
            _allPlayers = null;
            _loot = null;
            _exfils = null;
            _grenades = null;
            _tripwires = null;
            _questItems.Clear();
        }

        private void OnMatchStart()
        {
            InitializeDirectXResources();
        }

        private void RenderOverlay()
        {
            _textBatch.Clear();
            _lineBatch.Clear();

            WriteTopLeftText("Tarkov Overlay", Color.White, 13, "Tarkov");

            if (_localPlayer is null)
            {
                WriteTopLeftText("NOT IN RAID", Color.Red, 13, "Tarkov", 10, 30);
                return;
            }

            if (_isInGame)
            {
                WriteTopLeftText("IN RAID", Color.LimeGreen, 13, "Tarkov", 10, 30);
                WriteTopLeftText("Mem/s: " + Memory.Ticks, Color.White, 13, "Tarkov", 10, 50);

                if (_config.ToggleESP)
                {
                    RenderPlayers(_localPlayer);
                    RenderLoot(_localPlayer);
                    RenderWorldObjects(_localPlayer);

                    RenderTextBatch();
                    RenderLineBatch();
                }
            }
            else
            {
                WriteTopLeftText("NOT IN RAID", Color.Red, 13, "Tarkov", 10, 30);
            }
        }

        private void RenderPlayers(Player localPlayer)
        {
            var allPlayers = _allPlayers?.Select(x => x.Value).ToList();
            if (allPlayers is null) return;

            var localPlayerPos = localPlayer.Position;
            var enemyPositions = new List<Vector3>(12);
            var screenCoords = new List<Vector3>(12);

            foreach (var player in allPlayers)
            {
                var dist = Vector3.Distance(localPlayerPos, player.Position);
                if (!ShouldRenderPlayer(player, dist)) continue;

                enemyPositions.Clear();
                screenCoords.Clear();

                PopulatePlayerPositions(player, enemyPositions);
                WorldToScreenCombined(player, enemyPositions, screenCoords);

                RenderPlayerESP(player, dist, screenCoords);
            }
        }

        private void RenderLoot(Player localPlayer)
        {
            if (_loot?.Filter is not null)
            {
                foreach (var item in _loot.Filter)
                {
                    if (item is LootItem lootItem)
                    {
                        RenderLootItemESP(lootItem, localPlayer);
                    }
                    else if (item is LootContainer lootContainer)
                    {
                        RenderLootContainerESP(lootContainer, localPlayer);
                    }
                    else if (item is LootCorpse lootCorpse)
                    {
                        RenderLootCorpseESP(lootCorpse, localPlayer);
                    }
                }
            }

            if (_questItems?.Count > 0)
            {
                foreach (var questItem in _questItems)
                {
                    RenderQuestItemESP(questItem, localPlayer);
                }
            }
        }

        private void RenderLootItemESP(LootItem lootItem, Player localPlayer)
        {
            var lootDist = Vector3.Distance(localPlayer.Position, lootItem.Position);

            if (!_isLootItemOn || lootDist > _lootItemLimit)
                return;

            if (WorldToScreenLootTest(localPlayer, lootItem.Position, out var lootCoords))
            {
                _textBatch.Add((
                    $"{lootItem.GetFormattedValueShortName()}{Environment.NewLine}{Math.Round(lootDist, 0)}m",
                    (int)lootCoords.X + 5,
                    (int)lootCoords.Y - 25,
                    Color.DeepSkyBlue
                ));
            }
        }

        private void RenderLootContainerESP(LootContainer lootContainer, Player localPlayer)
        {
            var lootDist = Vector3.Distance(localPlayer.Position, lootContainer.Position);

            if (!_isLootContainerOn || lootDist > _lootContainerLimit)
                return;

            if (WorldToScreenLootTest(localPlayer, lootContainer.Position, out var lootCoords))
            {
                _textBatch.Add((
                    $"{lootContainer.Name}{Environment.NewLine}{Math.Round(lootDist, 0)}m",
                    (int)lootCoords.X + 5,
                    (int)lootCoords.Y - 25,
                    Color.Aqua
                ));
            }
        }

        private void RenderLootCorpseESP(LootCorpse lootCorpse, Player localPlayer)
        {
            var lootDist = Vector3.Distance(localPlayer.Position, lootCorpse.Position);

            if (!_isLootCorpseOn || lootDist > _lootCorpseLimit)
                return;

            if (WorldToScreenLootTest(localPlayer, lootCorpse.Position, out var lootCoords))
            {
                _textBatch.Add((
                    $"{lootCorpse.Name}{Environment.NewLine}{Math.Round(lootDist, 0)}m",
                    (int)lootCoords.X + 5,
                    (int)lootCoords.Y - 25,
                    Color.DeepPink
                ));
            }
        }

        private void RenderQuestItemESP(QuestItem questItem, Player localPlayer)
        {
            if (!_isQuestItemOn || questItem.Complete) return;

            var questDist = Vector3.Distance(localPlayer.Position, questItem.Position);

            if (questDist > _questItemLimit) return;

            if (WorldToScreenLootTest(localPlayer, questItem.Position, out var questItemCoords))
            {
                _textBatch.Add((
                    $"{questItem.Name}{Environment.NewLine}{Math.Round(questDist, 0)}m",
                    (int)questItemCoords.X + 5,
                    (int)questItemCoords.Y - 25,
                    Color.HotPink
                ));
            }
        }

        private void RenderWorldObjects(Player localPlayer)
        {
            var objectPositions = new List<Vector3>();
            var screenCoords = new List<Vector3>();

            GatherWorldObjectPositions(objectPositions);
            WorldToScreenCombined(localPlayer, objectPositions, screenCoords);

            RenderExfils(localPlayer, screenCoords);
            RenderGrenades(localPlayer, screenCoords);
            RenderTripwires(localPlayer, screenCoords);
        }

        private void GatherWorldObjectPositions(List<Vector3> positions)
        {
            if (_exfils is not null)
            {
                foreach (var exfil in _exfils)
                {
                    positions.Add(new Vector3(exfil.Position.X, exfil.Position.Y, exfil.Position.Z));
                }
            }

            if (_grenades is not null)
            {
                foreach (var grenade in _grenades)
                {
                    positions.Add(new Vector3(grenade.Position.X, grenade.Position.Y, grenade.Position.Z));
                }
            }

            if (_tripwires is not null)
            {
                foreach (var tripwire in _tripwires)
                {
                    positions.Add(new Vector3(tripwire.FromPos.X, tripwire.FromPos.Y, tripwire.FromPos.Z));
                    positions.Add(new Vector3(tripwire.ToPos.X, tripwire.ToPos.Y, tripwire.ToPos.Z));
                }
            }
        }

        private void RenderExfils(Player localPlayer, List<Vector3> screenCoords)
        {
            if (_exfils is null) return;

            int index = 0;
            foreach (var exfil in _exfils)
            {
                var exfilCoords = screenCoords[index++];
                if (exfilCoords.X > 0 || exfilCoords.Y > 0 || exfilCoords.Z > 0)
                {
                    _textBatch.Add((exfil.Name, (int)exfilCoords.X + 5, (int)exfilCoords.Y - 25, Color.LimeGreen));
                }
            }
        }

        private void RenderGrenades(Player localPlayer, List<Vector3> screenCoords)
        {
            if (_grenades is null) return;

            int index = 0;
            foreach (var grenade in _grenades)
            {
                var grenadeCoords = screenCoords[index++];
                if (grenadeCoords.X > 0 || grenadeCoords.Y > 0 || grenadeCoords.Z > 0)
                {
                    _textBatch.Add(("Grenade", (int)grenadeCoords.X + 5, (int)grenadeCoords.Y - 25, Color.Red));
                }
            }
        }

        private void RenderTripwires(Player localPlayer, List<Vector3> screenCoords)
        {
            if (_tripwires is null) return;

            int index = 0;
            foreach (var tripwire in _tripwires)
            {
                var fromCoords = screenCoords[index++];
                var toCoords = screenCoords[index++];

                if (fromCoords.X > 0 && fromCoords.Y > 0 && toCoords.X > 0 && toCoords.Y > 0)
                {
                    _lineBatch.Add(((int)fromCoords.X, (int)fromCoords.Y, (int)toCoords.X, (int)toCoords.Y, Color.Red));
                    _textBatch.Add(("Tripwire", (int)((fromCoords.X + toCoords.X) / 2), (int)((fromCoords.Y + toCoords.Y) / 2 - 25), Color.White));
                }
            }
        }

        private bool ShouldRenderPlayer(Player player, float dist)
        {
            return player.IsAlive &&
                   player.Type is not PlayerType.LocalPlayer &&
                   (
                       (player.IsHuman && dist <= _config.PlayerDist && _config.ToggleESP) ||
                       (!player.IsHuman && dist <= _config.PlayerDist && _config.ToggleESP) ||
                       (player.Type is PlayerType.Teammate && dist <= _config.PlayerDist && _config.ToggleESP)
                   );
        }

        private void PopulatePlayerPositions(Player player, List<Vector3> positions)
        {
            positions.AddRange(new[]
            {
                player.Position,
                player.HeadPosition,
                player.Spine3Position,
                player.LPalmPosition,
                player.RPalmPosition,
                player.PelvisPosition,
                player.LFootPosition,
                player.RFootPosition,
                player.LForearm1Position,
                player.RForearm1Position,
                player.LCalfPosition,
                player.RCalfPosition
            });
        }

        private void RenderPlayerESP(Player player, float dist, List<Vector3> coords)
        {
            var baseCoords = coords[0];
            var headCoords = coords[1];

            float boxHeight = headCoords.Y - baseCoords.Y;
            float boxWidth = boxHeight * 0.6f;
            float paddingHeight = boxHeight * 0.1f;
            float paddingWidth = boxWidth * 0.05f;
            boxHeight += paddingHeight;
            boxWidth += paddingWidth;

            string name = "ERROR";
            string distance = "ERROR";
            Color color = Color.White;

            switch (player.Type)
            {
                case PlayerType.BEAR or PlayerType.USEC when _config.PlayerESP && dist <= _config.PlayerDist:
                    if (_config.ToggleESP && _config.BoneESP && dist <= _config.BoneLimit)
                    {
                        DrawSkeletonLines(coords, Color.Red);
                    }
                    name = player.Name;
                    distance = $"[{Math.Round(dist, 0)}m]";
                    color = Color.White;
                    break;

                case PlayerType.Scav when _config.ScavESP && dist <= _config.ScavDist:
                    if (_config.ToggleESP && _config.BoneESP && dist <= _config.BoneLimit)
                    {
                        DrawSkeletonLines(coords, Color.Yellow);
                    }
                    name = "Scav";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    color = Color.Yellow;
                    break;

                case PlayerType.PlayerScav when _config.PlayerESP && dist <= _config.PlayerDist:
                    if (_config.ToggleESP && _config.BoneESP && dist <= _config.BoneLimit)
                    {
                        DrawSkeletonLines(coords, Color.Cyan);
                    }
                    name = "Player Scav";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    color = Color.Cyan;
                    break;

                case PlayerType.Boss when _config.BossESP && dist <= _config.BossDist:
                    if (_config.ToggleESP && _config.BoneESP && dist <= _config.BoneLimit)
                    {
                        DrawSkeletonLines(coords, Color.DarkOrange);
                    }
                    name = player.Name;
                    distance = $"[{Math.Round(dist, 0)}m]";
                    color = Color.DarkOrange;
                    break;

                case PlayerType.BossFollower or PlayerType.BossGuard when _config.ScavESP && dist <= _config.ScavDist:
                    if (_config.ToggleESP && _config.BoneESP && dist <= _config.BoneLimit)
                    {
                        DrawSkeletonLines(coords, Color.Orange);
                    }
                    name = "Follower";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    color = Color.Orange;
                    break;

                case PlayerType.Teammate when _config.TeamESP && dist <= _config.TeamDist:
                    if (_config.ToggleESP && _config.BoneESP && dist <= _config.BoneLimit)
                    {
                        DrawSkeletonLines(coords, Color.LimeGreen);
                    }
                    name = player.Name;
                    distance = $"[{Math.Round(dist, 0)}m]";
                    color = Color.LimeGreen;
                    break;

                case PlayerType.Cultist when _config.ScavESP && dist <= _config.ScavDist:
                    if (_config.ToggleESP && _config.BoneESP && dist <= _config.BoneLimit)
                    {
                        DrawSkeletonLines(coords, Color.Purple);
                    }
                    name = "Cultist";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    color = Color.Purple;
                    break;

                case PlayerType.Raider when _config.ScavESP && dist <= _config.ScavDist:
                    if (_config.ToggleESP && _config.BoneESP && dist <= _config.BoneLimit)
                    {
                        DrawSkeletonLines(coords, Color.Orange);
                    }
                    name = "Raider";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    color = Color.Orange;
                    break;
            }

            _textBatch.Add((name, (int)(baseCoords.X - (boxWidth / 2)), (int)(baseCoords.Y + boxHeight) - 35, color));
            _textBatch.Add((distance, (int)(baseCoords.X - (boxWidth / 2)), (int)(baseCoords.Y + boxHeight) - 20, color));
        }

        private void WriteText(string msg, int x, int y, System.Drawing.Color color, float fontSize = 13, string fontFamily = "Arial")
        {
            var sharpDxColor = new SharpDX.Color(color.R, color.G, color.B, color.A);

            _font.DrawText(_sprite, msg, x, y, sharpDxColor);
        }

        private void WriteTopLeftText(string msg, Color color, float fontSize = 13, string fontFamily = "Arial", int xOffset = 10, int yOffset = 10)
        {
            WriteText(msg, xOffset, yOffset, color, fontSize, fontFamily);
        }

        private void DrawLine(int x1, int y1, int x2, int y2, Color color)
        {
            bool previousZEnable = _device.GetRenderState<bool>(RenderState.ZEnable);
            bool previousLighting = _device.GetRenderState<bool>(RenderState.Lighting);

            try
            {
                _device.SetRenderState(RenderState.ZEnable, false);
                _device.SetRenderState(RenderState.Lighting, false);
                _device.SetRenderState(RenderState.CullMode, Cull.None);
                _device.SetRenderState(RenderState.AlphaBlendEnable, true);
                _device.SetRenderState(RenderState.SourceBlend, Blend.SourceAlpha);
                _device.SetRenderState(RenderState.DestinationBlend, Blend.InverseSourceAlpha);

                var vertices = new[]
                {
            new Vertex
            {
                Position = new SharpDX.Vector3(x1, y1, 0.1f),
                Color = new SharpDX.ColorBGRA(color.R, color.G, color.B, color.A)
            },
            new Vertex
            {
                Position = new SharpDX.Vector3(x2, y2, 0.1f),
                Color = new SharpDX.ColorBGRA(color.R, color.G, color.B, color.A)
            }
        };

                using (var vertexBuffer = new VertexBuffer(_device,
                       Vertex.Stride * 2,
                       Usage.WriteOnly,
                       Vertex.Format,
                       Pool.Default))
                {
                    vertexBuffer.Lock(0, 0, LockFlags.None).WriteRange(vertices);
                    vertexBuffer.Unlock();

                    _device.SetTransform(TransformState.World, Matrix.Identity);
                    _device.SetTransform(TransformState.View, Matrix.Identity);
                    _device.SetTransform(TransformState.Projection,
                        Matrix.OrthoOffCenterLH(0, _device.Viewport.Width, _device.Viewport.Height, 0, 0.0f, 1.0f));

                    _device.SetStreamSource(0, vertexBuffer, 0, Vertex.Stride);
                    _device.VertexFormat = Vertex.Format;
                    _device.DrawPrimitives(PrimitiveType.LineList, 0, 1);
                }
            }
            finally
            {
                _device.SetRenderState(RenderState.ZEnable, previousZEnable);
                _device.SetRenderState(RenderState.Lighting, previousLighting);
            }
        }

        private void DrawSkeletonLines(List<Vector3> coords, Color color)
        {
            int[][] connections = new int[][]
            {
                new int[] { 1, 2 },   // Head to Spine
                new int[] { 2, 5 },   // Spine to Pelvis
                new int[] { 2, 8 },   // Spine to Left Forearm
                new int[] { 8, 3 },   // Left Forearm to Left Palm
                new int[] { 2, 9 },   // Spine to Right Forearm
                new int[] { 9, 4 },   // Right Forearm to Right Palm
                new int[] { 5, 10 },  // Pelvis to Left Calf
                new int[] { 10, 6 },  // Left Calf to Left Foot
                new int[] { 5, 11 },  // Pelvis to Right Calf
                new int[] { 11, 7 }   // Right Calf to Right Foot
            };

            foreach (var connection in connections)
            {
                _lineBatch.Add(((int)coords[connection[0]].X, (int)coords[connection[0]].Y, (int)coords[connection[1]].X, (int)coords[connection[1]].Y, color));
            }
        }

        private void SafeEndDraw()
        {
            try
            {
                _device.EndScene();
                _device.Present();
            }
            catch { }
        }

        private void ClosedOverlay(object sender, FormClosingEventArgs e)
        {
            try
            {
                _isRunning = false;

                _device.EndScene();
                _device.Present();
                _device.Dispose();
                _device = null;
            }
            catch { }
        }

        private bool WorldToScreenCombined(Player player, List<Vector3> enemyPositions, List<Vector3> screenCoords)
        {
            screenCoords.Clear();

            System.Drawing.Rectangle bounds = this.Bounds;
            int width = bounds.Width;
            int height = bounds.Height;

            var temp = Matrix4x4.Transpose(Memory.CameraManager.ViewMatrix);
            var translationVector = new Vector3(temp.M41, temp.M42, temp.M43);
            var up = new Vector3(temp.M21, temp.M22, temp.M23);
            var right = new Vector3(temp.M11, temp.M12, temp.M13);

            foreach (var enemyPos in enemyPositions)
            {
                var w = D3DXVec3Dot(translationVector, enemyPos) + temp.M44;
                if (w < 0.098f)
                {              
                    screenCoords.Add(new Vector3(0, 0, 0)); 
                    continue;
                }

                var y = D3DXVec3Dot(up, enemyPos) + temp.M24;
                var x = D3DXVec3Dot(right, enemyPos) + temp.M14;
                var screenX = width / 2 * (1f + x / w);
                var screenY = height / 2 * (1f - y / w);

                screenCoords.Add(new Vector3(screenX, screenY, w));
            }
            return true;
        }

        private bool WorldToScreenLootTest(Player player, Vector3 itemPos, out System.Numerics.Vector2 screenPos)
        {
            screenPos = new System.Numerics.Vector2(0, 0);

            System.Drawing.Rectangle bounds = this.Bounds;
            int width = bounds.Width;
            int height = bounds.Height;

            var temp = Matrix4x4.Transpose(Memory.CameraManager.ViewMatrix);
            Vector3 translationVector = new Vector3(temp.M41, temp.M42, temp.M43);
            Vector3 up = new Vector3(temp.M21, temp.M22, temp.M23);
            Vector3 right = new Vector3(temp.M11, temp.M12, temp.M13);

            float w = D3DXVec3Dot(translationVector, itemPos) + temp.M44;

            if (w < 0.098f)
                return false;

            float y = D3DXVec3Dot(up, itemPos) + temp.M24;
            float x = D3DXVec3Dot(right, itemPos) + temp.M14;

            screenPos.X = (width / 2) * (1f + x / w);
            screenPos.Y = (height / 2) * (1f - y / w);

            return true;
        }

        private float D3DXVec3Dot(Vector3 v1, Vector3 v2)
        {
            return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
        }

        private void RenderTextBatch()
        {
            foreach (var (msg, x, y, color) in _textBatch)
            {
                WriteText(msg, x, y, color);
            }
        }

        private void RenderLineBatch()
        {
            foreach (var (x1, y1, x2, y2, color) in _lineBatch)
            {
                DrawLine(x1, y1, x2, y2, color);
            }
        }
    }
}