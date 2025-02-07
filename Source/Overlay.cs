using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.AspNetCore.Http;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using AlphaMode = SharpDX.Direct2D1.AlphaMode;
using Color = System.Drawing.Color;
using Factory = SharpDX.Direct2D1.Factory;
using Font = System.Drawing.Font;
using FontFactory = SharpDX.DirectWrite.Factory;
using Point = System.Drawing.Point;
using TextAntialiasMode = SharpDX.Direct2D1.TextAntialiasMode;
using TextRenderer = System.Windows.Forms.TextRenderer;
using Vector3 = System.Numerics.Vector3;

namespace eft_dma_radar
{
    public partial class Overlay : Form
    {
        // Constants
        private const int TargetFrameRate = 144;
        private const int TargetFrameTime = 1000 / TargetFrameRate;

        // Configuration
        private Config _config;
        private BrushManager _brushManager;
        private WindowRenderTarget _device;
        private HwndRenderTargetProperties _renderProperties;
        private readonly FontFactory _fontFactory = new();
        private Thread _directXThread;
        private CancellationTokenSource _cancellationTokenSource;
        private CancellationToken _cancellationToken;
        private Factory _factory;
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

        // ESP Settings
        public static bool IsESPOn = true;
        public static bool IsBoneESPOn = true;
        public static bool IsPMCOn = true;
        public static bool IsTeamOn = true;
        public static bool IsScavOn = true;
        public static bool IsPlayerScavOn = true;
        public static int BoneLimit = 300;
        public static int ScavLimit = 300;
        public static int PlayerLimit = 300;
        public static int TeamLimit = 300;

        // Loot ESP Settings
        private bool _isLootItemOn = true;
        private float _lootItemLimit = 250f;
        private bool _isLootContainerOn = true;
        private float _lootContainerLimit = 50f;
        private bool _isLootCorpseOn = true;
        private float _lootCorpseLimit = 80f;
        private bool _isQuestItemOn = true;
        private float _questItemLimit = 150f;

        public Overlay()
        {
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
            _factory?.Dispose();
            _brushManager?.Dispose();

            _brushManager = null;
            _device = null;
            _factory = null;
            _cancellationTokenSource = null;
            _directXThread = null;
        }

        private void InitializeDirectXResources()
        {
            _factory = new Factory();
            _renderProperties = new HwndRenderTargetProperties
            {
                Hwnd = Handle,
                PixelSize = new Size2(Width, Height),
                PresentOptions = PresentOptions.None
            };

            _device = new WindowRenderTarget(
                _factory,
                new RenderTargetProperties(
                    new PixelFormat(Format.B8G8R8A8_UNorm, AlphaMode.Premultiplied)
                ),
                _renderProperties
            );

            _brushManager = new BrushManager(_device);
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

                    _device.BeginDraw();
                    _device.Clear(SharpDX.Color.Transparent);
                    _device.TextAntialiasMode = TextAntialiasMode.Aliased;

                    UpdateGameState();
                    RenderOverlay();

                    _device.Flush();
                    _device.EndDraw();

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
            // Clear cached data
            _allPlayers = null;
            _loot = null;
            _exfils = null;
            _grenades = null;
            _tripwires = null;
            _questItems.Clear();
        }

        private void OnMatchStart()
        {
            // Reinitialize resources if needed
            InitializeDirectXResources();
        }

        private void RenderOverlay()
        {
            WriteTopLeftText("Tarkov Overlay", "WHITE", 13, "Tarkov");
            WriteTopRightText("Mem/s: " + Memory.Ticks, "WHITE", 13, "Tarkov");

            if (_localPlayer is null)
            {
                WriteTopLeftText("NOT IN RAID", "RED", 13, "Tarkov-Regular", 10, 30);
                return;
            }

            if (_isInGame)
            {
                WriteTopLeftText("IN RAID", "GREEN", 13, "Tarkov-Regular", 10, 30);
                RenderPlayers(_localPlayer);
                RenderLoot(_localPlayer);
                RenderWorldObjects(_localPlayer);
            }
            else
            {
                WriteTopLeftText("NOT IN RAID", "RED", 13, "Tarkov-Regular", 10, 30);
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
                WriteText( 
                    $"{lootItem.GetFormattedValueShortName()}{Environment.NewLine}{Math.Round(lootDist, 0)}m",
                    lootCoords.X + 5,
                    lootCoords.Y - 25,
                    "LOOSE_LOOT"
                );
            }
        }

        private void RenderLootContainerESP(LootContainer lootContainer, Player localPlayer)
        {
            var lootDist = Vector3.Distance(localPlayer.Position, lootContainer.Position);

            if (!_isLootContainerOn || lootDist > _lootContainerLimit)
                return;

            if (WorldToScreenLootTest(localPlayer, lootContainer.Position, out var lootCoords))
            {
                WriteText( 
                    $"{lootContainer.Name}{Environment.NewLine}{Math.Round(lootDist, 0)}m",
                    lootCoords.X + 5,
                    lootCoords.Y - 25,
                    "CONTAINER_LOOT"
                );
            }
        }

        private void RenderLootCorpseESP(LootCorpse lootCorpse, Player localPlayer)
        {
            var lootDist = Vector3.Distance(localPlayer.Position, lootCorpse.Position);

            if (!_isLootCorpseOn || lootDist > _lootCorpseLimit)
                return;

            if (WorldToScreenLootTest(localPlayer, lootCorpse.Position, out var lootCoords))
            {
                WriteText(
                    $"{lootCorpse.Name}{Environment.NewLine}{Math.Round(lootDist, 0)}m",
                    lootCoords.X + 5,
                    lootCoords.Y - 25,
                    "CORPSE");
            }
        }

        private void RenderQuestItemESP(QuestItem questItem, Player localPlayer)
        {
            if (!_isQuestItemOn || questItem.Complete) return;

            var questDist = Vector3.Distance(localPlayer.Position, questItem.Position);

            if (questDist > _questItemLimit) return;

            if (WorldToScreenLootTest(localPlayer, questItem.Position, out var questItemCoords))
            {
                WriteText(
                    $"{questItem.Name}{Environment.NewLine}{Math.Round(questDist, 0)}m",
                    questItemCoords.X + 5,
                    questItemCoords.Y - 25,
                    "QUEST");
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
                    WriteText(exfil.Name, exfilCoords.X + 5, exfilCoords.Y - 25, "GREEN", 13, "Tarkov-Regular");
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
                    WriteText("Grenade", grenadeCoords.X + 5, grenadeCoords.Y - 25, "RED", 13, "Tarkov-Regular");
                }
            }
        }

        private void RenderTripwires(Player localPlayer, List<Vector3> screenCoords)
        {
            if (_tripwires is null) return;

            var brush = _brushManager.GetBrush("RED");
            int index = 0;

            foreach (var tripwire in _tripwires)
            {
                var fromCoords = screenCoords[index++];
                var toCoords = screenCoords[index++];

                if (fromCoords.X > 0 && fromCoords.Y > 0 && toCoords.X > 0 && toCoords.Y > 0)
                {
                    _device.DrawLine(new RawVector2(fromCoords.X, fromCoords.Y), new RawVector2(toCoords.X, toCoords.Y), brush);
                    WriteText("Tripwire", (fromCoords.X + toCoords.X) / 2, (fromCoords.Y + toCoords.Y) / 2 - 25, "WHITE", 13, "Tarkov-Regular");
                }
            }
        }

        private bool ShouldRenderPlayer(Player player, float dist)
        {
            return player.IsAlive &&
                   player.Type is not PlayerType.LocalPlayer &&
                   (
                       (player.IsHuman && dist <= PlayerLimit && IsESPOn) ||
                       (!player.IsHuman && dist <= ScavLimit && IsESPOn) ||
                       (player.Type is PlayerType.Teammate && dist <= TeamLimit && IsESPOn)
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
            string brushName = "INVALID_COLOR";
            string fontFamily = "Tarkov-Regular";
            int fontSize = 13;

            switch (player.Type)
            {
                case PlayerType.BEAR or PlayerType.USEC when IsPMCOn && dist <= PlayerLimit:
                    if (IsESPOn && IsBoneESPOn && dist <= BoneLimit)
                    {
                        DrawSkeletonLines(coords, "RED");
                    }
                    name = player.Name;
                    distance = $"[{Math.Round(dist, 0)}m]";
                    brushName = "WHITE";
                    break;

                case PlayerType.Scav when IsScavOn && dist <= ScavLimit:
                    if (IsESPOn && IsBoneESPOn && dist <= BoneLimit)
                    {
                        DrawSkeletonLines(coords, "YELLOW");
                    }
                    name = "Scav";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    brushName = "YELLOW";
                    break;

                case PlayerType.PlayerScav when IsPlayerScavOn && dist <= PlayerLimit:
                    if (IsESPOn && IsBoneESPOn && dist <= BoneLimit)
                    {
                        DrawSkeletonLines(coords, "CYAN");
                    }
                    name = "Player Scav";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    brushName = "CYAN";
                    break;

                case PlayerType.Boss when IsScavOn && dist <= ScavLimit:
                    if (IsESPOn && IsBoneESPOn && dist <= BoneLimit)
                    {
                        DrawSkeletonLines(coords, "ORANGE");
                    }
                    name = player.Name;
                    distance = $"[{Math.Round(dist, 0)}m]";
                    brushName = "ORANGE";
                    break;

                case PlayerType.BossFollower or PlayerType.BossGuard when IsScavOn && dist <= ScavLimit:
                    if (IsESPOn && IsBoneESPOn && dist <= BoneLimit)
                    {
                        DrawSkeletonLines(coords, "ORANGE");
                    }
                    name = "Follower";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    brushName = "ORANGE";
                    break;

                case PlayerType.Teammate when IsTeamOn && dist <= TeamLimit:
                    if (IsESPOn && IsBoneESPOn && dist <= BoneLimit)
                    {
                        DrawSkeletonLines(coords, "TEAMMATE");
                    }
                    name = player.Name;
                    distance = $"[{Math.Round(dist, 0)}m]";
                    brushName = "TEAMMATE";
                    break;

                case PlayerType.Cultist when IsScavOn && dist <= ScavLimit:
                    if (IsESPOn && IsBoneESPOn && dist <= BoneLimit)
                    {
                        DrawSkeletonLines(coords, "PURPLE");
                    }
                    name = "Cultist";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    brushName = "PURPLE";
                    break;

                case PlayerType.Raider when IsScavOn && dist <= ScavLimit:
                    if (IsESPOn && IsBoneESPOn && dist <= BoneLimit)
                    {
                        DrawSkeletonLines(coords, "ORANGE");
                    }
                    name = "Raider";
                    distance = $"[{Math.Round(dist, 0)}m]";
                    brushName = "ORANGE";
                    break;
            }

            using (var textFormat = new TextFormat(_fontFactory, fontFamily, fontSize))
            {
                var nameLayout = new TextLayout(_fontFactory, name, textFormat, float.MaxValue, float.MaxValue);
                float nameWidth = nameLayout.Metrics.Width;
                float nameStartX = baseCoords.X - (nameWidth / 2);

                var distanceLayout = new TextLayout(_fontFactory, distance, textFormat, float.MaxValue, float.MaxValue);
                float distanceWidth = distanceLayout.Metrics.Width;
                float distanceStartX = baseCoords.X - (distanceWidth / 2);

                WriteText(name, nameStartX, (baseCoords.Y + boxHeight) - 35, brushName, fontSize, fontFamily);
                WriteText(distance, distanceStartX, (baseCoords.Y + boxHeight) - 20, brushName, fontSize, fontFamily);
            }
        }

        private void WriteText(string msg, float x, float y, string brushName, float fontSize = 13, string fontFamily = "Arial Unicode MS")
        {
            var brush = _brushManager.GetBrush(brushName);
            var measure = PredictSize(msg, fontSize, fontFamily);
            _device.DrawText(
                msg,
                new TextFormat(_fontFactory, fontFamily, fontSize),
                new RawRectangleF(x, y, x + measure.Width, y + measure.Height),
                brush
            );
        }

        private void WriteTopLeftText(string msg, string brushName, float fontSize = 13, string fontFamily = "Arial Unicode MS", float xOffset = 10, float yOffset = 10)
        {
            var x = xOffset;
            var y = yOffset;
            WriteText(msg, x, y, brushName, fontSize, fontFamily);
        }

        private void WriteTopRightText(string msg, string brushName, float fontSize = 13, string fontFamily = "Arial Unicode MS", float xOffset = 10, float yOffset = 10)
        {
            var y = yOffset;
            var measure = PredictSize(msg, fontSize, fontFamily);
            var x = Width - measure.Width - xOffset; // Aligns the right side of the text
            WriteText(msg, x, y, brushName, fontSize, fontFamily);
        }

        private void WriteCenterText(string msg, float y, string brushName, float fontSize = 13, string fontFamily = "Arial Unicode MS")
        {
            var measure = PredictSize(msg, fontSize, fontFamily);
            var x = Width / 2 - measure.Width / 2;
            WriteText(msg, x, y, brushName, fontSize, fontFamily);
        }

        private void WriteBottomText(string msg, float x, string brushName, float fontSize = 13, string fontFamily = "Arial Unicode MS")
        {
            var measure = PredictSize(msg, fontSize, fontFamily);
            var y = Height - measure.Height;
            WriteText(msg, x, y, brushName, fontSize, fontFamily);
        }

        private Size PredictSize(string msg, float fontSize = 13, string fontFamily = "Arial Unicode MS")
        {
            return TextRenderer.MeasureText(msg, new Font(fontFamily, fontSize - 3));
        }

        private void DrawSkeletonLines(List<Vector3> coords, string brushName)
        {
            var brush = _brushManager.GetBrush(brushName);

            // Predefined skeleton line connections
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
                _device.DrawLine(
                    new RawVector2(coords[connection[0]].X, coords[connection[0]].Y),
                    new RawVector2(coords[connection[1]].X, coords[connection[1]].Y),
                    brush, 2.0f
                );
            }
        }

        private void SafeEndDraw()
        {
            try
            {
                _device.Flush();
                _device.EndDraw();
            }
            catch { }
        }

        private void ClosedOverlay(object sender, FormClosingEventArgs e)
        {
            try
            {
                _isRunning = false;

                _device.Flush();
                _device.EndDraw();
                _factory.Dispose();
                _device.Dispose();
                _device = null;
            }
            catch { }
        }

        private bool WorldToScreenCombined(Player player, List<Vector3> enemyPositions, List<Vector3> screenCoords)
        {
            screenCoords.Clear(); // Clear previous results

            int width = Screen.PrimaryScreen.Bounds.Width;
            int height = Screen.PrimaryScreen.Bounds.Height;

            var temp = Matrix4x4.Transpose(Memory.CameraManager.ViewMatrix);
            var translationVector = new Vector3(temp.M41, temp.M42, temp.M43);
            var up = new Vector3(temp.M21, temp.M22, temp.M23);
            var right = new Vector3(temp.M11, temp.M12, temp.M13);

            foreach (var enemyPos in enemyPositions)
            {
                var w = D3DXVec3Dot(translationVector, enemyPos) + temp.M44;
                if (w < 0.098f)
                {
                    // Skip points behind the camera
                    screenCoords.Add(new Vector3(0, 0, 0)); // Or choose an appropriate default or skip
                    continue;
                }

                var y = D3DXVec3Dot(up, enemyPos) + temp.M24;
                var x = D3DXVec3Dot(right, enemyPos) + temp.M14;
                var screenX = width / 2 * (1f + x / w);
                var screenY = height / 2 * (1f - y / w);

                // Add the calculated screen coordinates
                screenCoords.Add(new Vector3(screenX, screenY, w));
            }
            return true;
        }

        private bool WorldToScreenLootTest(Player player, Vector3 itemPos, out System.Numerics.Vector2 screenPos)
        {
            screenPos = new System.Numerics.Vector2(0, 0);

            // Get the primary screen dimensions
            int width = Screen.PrimaryScreen.Bounds.Width;
            int height = Screen.PrimaryScreen.Bounds.Height;

            var temp = Matrix4x4.Transpose(Memory.CameraManager.ViewMatrix);
            Vector3 translationVector = new Vector3(temp.M41, temp.M42, temp.M43);
            Vector3 up = new Vector3(temp.M21, temp.M22, temp.M23);
            Vector3 right = new Vector3(temp.M11, temp.M12, temp.M13);

            float w = D3DXVec3Dot(translationVector, itemPos) + temp.M44;

            // Early return if behind camera
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
    }
}