using System.Collections.ObjectModel;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
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
using Numerics = System.Numerics;
using System.Xml.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

namespace eft_dma_radar
{
    public partial class Overlay : Form
    {
        private frmMain _frmMain;

        public static bool isESPOn = true;
        public static bool isBoneESPOn = true;
        public static bool isPMCOn = true;
        public static bool isTeamOn = true;
        public static bool isScavOn = true;
        public static bool isPlayerScavOn = true;
        public static int boneLimit = 300;
        public static int scavLimit = 300;
        public static int playerLimit = 300;
        public static int teamLimit = 300;

        // User-defined settings for LootItem
        bool isLootItemOn = true;
        float lootItemLimit = 250f;      

        // User-defined settings for LootContainer
        bool isLootContainerOn = true;
        float lootContainerLimit = 50f;        

        // User-defined settings for LootCorpse
        bool isLootCorpseOn = true;
        float lootCorpseLimit = 80f;        

        // User-defined settings for QuestItem
        bool isQuestItemOn = true;
        float questItemLimit = 150f;        

        // Loot ESP
        private LootManager Loot
        {
            get => Memory.Loot;
        }
        private List<Exfil> Exfils
        {
            get => Memory.Exfils;
        }
        private List<Grenade> Grenades
        {
            get => Memory.Grenades;
        }
        private List<Tripwire> Tripwires
        {
            get => Memory.Tripwires;
        }

        private void DirectXThread()
        {
            const int targetFrameRate = 144;
            const int targetFrameTime = 1000 / targetFrameRate; 

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            var isReady = Ready;
            var inGame = InGame;
            var localPlayer = LocalPlayer;

            // Preallocate lists to reduce memory allocation
            List<Vector3> enemyPositions = new(12);
            List<Vector3> coords = new(12);

            while (_running && !_token.IsCancellationRequested)
            {
                try
                {
                    stopwatch.Restart(); // Restart stopwatch at the beginning of each frame

                    _device.BeginDraw();
                    _device.Clear(SharpDX.Color.Transparent);
                    _device.TextAntialiasMode = TextAntialiasMode.Aliased;

                    WriteTopLeftText("Tarkov Overlay", "WHITE", 13, "Tarkov");
                    WriteTopRightText("Mem/s: " + Memory.Ticks, "WHITE", 13, "Tarkov");

                    if (localPlayer is null)
                    {
                        WriteTopLeftText("NOT IN RAID", "RED", 13, "Tarkov-Regular", 10, 30);
                        _device.Flush();
                        _device.EndDraw();
                        Thread.Sleep(5);
                        continue;
                    }

                    if (InGame)
                    {
                        WriteTopLeftText("IN RAID", "GREEN", 13, "Tarkov-Regular", 10, 30);
                        var allPlayers = AllPlayers?.Select(x => x.Value);

                        if (allPlayers is not null)
                        {
                            var localPlayerPos = localPlayer.Position;

                            foreach (var player in allPlayers)
                            {
                                // Optimize distance and ESP condition check
                                var dist = Vector3.Distance(localPlayerPos, player.Position);
                                if (!ShouldRenderPlayer(player, dist)) continue;

                                // Clear and reuse preallocated lists
                                enemyPositions.Clear();
                                coords.Clear();

                                // Populate positions efficiently
                                PopulatePlayerPositions(player, enemyPositions);

                                WorldToScreenCombined(player, enemyPositions, coords);

                                RenderPlayerESP(player, dist, coords);
                            }
                        }
                        RenderLootESP(localPlayer);
                        RenderWorldObjects(localPlayer);
                    }
                    else
                    {
                        WriteTopLeftText("NOT IN RAID", "RED", 13, "Tarkov-Regular", 10, 30);
                    }

                    _device.Flush();
                    _device.EndDraw();

                    // Calculate how much time was spent on the frame
                    var frameTime = stopwatch.ElapsedMilliseconds;

                    // Calculate how much sleep time is needed to achieve target frame time
                    var sleepTime = targetFrameTime - frameTime;
                    if (sleepTime > 0)
                    {
                        Thread.Sleep((int)sleepTime); // Sleep for the remaining time
                    }
                }
                catch (SharpDXException e)
                {
                    Console.WriteLine(e);
                    SafeEndDraw();
                }
            }
        }

        private bool ShouldRenderPlayer(Player player, float dist)
        {
            return (player.IsAlive &&
                    player.Type is not PlayerType.LocalPlayer &&
                    (
                        (player.IsHuman && dist <= playerLimit && isESPOn) ||
                        (!player.IsHuman && dist <= scavLimit && isESPOn) ||
                        (player.Type is PlayerType.Teammate && dist <= teamLimit && isESPOn)
                    ));
        }

        private void PopulatePlayerPositions(Player player, List<Vector3> positions)
        {
            positions.AddRange(new Vector3[]
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
            // Only need these 2 coords
            Vector3 baseCoords = coords[0];
            Vector3 headCoords = coords[1];

            // Precalculate box dimensions
            float boxHeight = headCoords.Y - baseCoords.Y;
            float boxWidth = boxHeight * 0.6f;
            float paddingHeight = boxHeight * 0.1f;
            float paddingWidth = boxWidth * 0.05f;
            boxHeight += paddingHeight;
            boxWidth += paddingWidth;

            // Player Text Variables
            string name = "ERROR";
            string distance = "ERROR";
            string brushName = "INVALID_COLOR";
            string fontFamily = "Tarkov-Regular";
            int fontSize = 13;

            #region PMC
            if ((player.Type is PlayerType.BEAR || player.Type is PlayerType.USEC) && isPMCOn && dist <= playerLimit)
            {

                if (isESPOn && isBoneESPOn && isPMCOn && dist <= boneLimit)
                {
                    DrawSkeletonLines(coords, "RED");
                }
                if (isESPOn && isPMCOn && dist <= playerLimit)
                {
                    name = player.Name;
                    distance = "[" + Math.Round(dist, 0) + "m]";
                    brushName = "WHITE";
                }
            }
            #endregion
            #region SCAV
            if (player.Type is PlayerType.Scav && isScavOn && dist <= scavLimit)
            {
                if (isESPOn && isBoneESPOn && isScavOn && dist <= boneLimit)
                {
                    DrawSkeletonLines(coords, "YELLOW");
                }
                if (isESPOn && isScavOn && dist <= scavLimit)
                {
                    name = "Scav";
                    distance = "[" + Math.Round(dist, 0) + "m]";
                    brushName = "YELLOW";
                }
            }
            #endregion
            #region PlayerScav
            if (player.Type is PlayerType.PlayerScav && isPMCOn && dist <= playerLimit)
            {
                if (isESPOn && isBoneESPOn && isPlayerScavOn && dist <= boneLimit)
                {
                    DrawSkeletonLines(coords, "CYAN");
                }
                if (isESPOn && isPlayerScavOn && dist <= playerLimit)
                {
                    name = "Player Scav";
                    distance = "[" + Math.Round(dist, 0) + "m]";
                    brushName = "CYAN";
                }
            }
            #endregion
            #region Boss
            if (player.Type is PlayerType.Boss && isScavOn && dist <= scavLimit)
            {
                if (isESPOn && isBoneESPOn && isScavOn && dist <= boneLimit)
                {
                    DrawSkeletonLines(coords, "ORANGE");
                }
                if (isESPOn && isScavOn && dist <= scavLimit)
                {
                    name = player.Name;
                    distance = "[" + Math.Round(dist, 0) + "m]";
                    brushName = "ORANGE";
                }
            }
            #endregion
            #region BossFollower
            if ((player.Type is PlayerType.BossFollower || player.Type is PlayerType.BossGuard) && isScavOn && dist <= scavLimit)
            {
                if (isESPOn && isBoneESPOn && isScavOn && dist <= boneLimit)
                {
                    DrawSkeletonLines(coords, "ORANGE");
                }
                if (isESPOn && isScavOn && dist <= scavLimit)
                {
                    name = "Follower";
                    distance = "[" + Math.Round(dist, 0) + "m]";
                    brushName = "ORANGE";
                }
            }
            #endregion
            #region Teammate
            if (player.Type is PlayerType.Teammate && isTeamOn && dist <= teamLimit)
            {
                if (isESPOn && isBoneESPOn && isTeamOn && dist <= boneLimit)
                {
                    DrawSkeletonLines(coords, "TEAMMATE");
                }
                if (isESPOn && isTeamOn && dist <= boneLimit)
                {
                    name = player.Name;
                    distance = "[" + Math.Round(dist, 0) + "m]";
                    brushName = "TEAMMATE";
                }
            }
            #endregion
            #region Cultist
            if (player.Type is PlayerType.Cultist && isScavOn && dist <= scavLimit)
            {
                if (isESPOn && isBoneESPOn && isScavOn && dist <= boneLimit)
                {
                    DrawSkeletonLines(coords, "PURPLE");
                }
                if (isESPOn && isScavOn && dist <= scavLimit)
                {
                    name = "Cultist";
                    distance = "[" + Math.Round(dist, 0) + "m]";
                    brushName = "PURPLE";
                }
            }
            #endregion
            #region Raider
            if (player.Type is PlayerType.Raider && isScavOn && dist <= scavLimit)
            {
                if (isESPOn && isBoneESPOn && isScavOn && dist <= boneLimit)
                {
                    DrawSkeletonLines(coords, "ORANGE");
                }
                if (isESPOn && isScavOn && dist <= scavLimit)
                {
                    name = "Raider";
                    distance = "[" + Math.Round(dist, 0) + "m]";
                    brushName = "ORANGE"; ;
                }
            }
            #endregion

            using (var textFormat = new TextFormat(_fontFactory, fontFamily, fontSize))
            {
                // Measure the width of the text
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

        // Render all loot types
        private void RenderLootESP(Player localPlayer)
        {
            var loot = Loot; // Already done

            if (loot?.Filter is not null)
            {
                var localPlayerPos = localPlayer.Position;

                foreach (var item in loot.Filter)
                {
                    var lootDist = Vector3.Distance(localPlayerPos, item.Position);

                    // Render LootItem
                    if (item is LootItem lootItem)
                    {
                        RenderLootItemESP(lootItem, localPlayer);
                    }

                    // Render LootContainer
                    if (item is LootContainer lootContainer)
                    {
                        RenderLootContainerESP(lootContainer, localPlayer);
                    }

                    // Render LootCorpse
                    if (item is LootCorpse lootCorpse)
                    {
                        RenderLootCorpseESP(lootCorpse, localPlayer);
                    }
                }
            }
            // Handle QuestItems separately, as they are not part of LootableObject
            if (QuestItems != null && QuestItems.Count > 0)
            {
                foreach (var questItem in QuestItems)
                {
                    RenderQuestItemESP(questItem, localPlayer);
                }
            }
        }

        // Render LootItem ESP
        private void RenderLootItemESP(LootItem lootItem, Player localPlayer)
        {
            var lootDist = Vector3.Distance(localPlayer.Position, lootItem.Position);

            if (!isLootItemOn || lootDist > lootItemLimit)
                return;

            if (WorldToScreenLootTest(localPlayer, lootItem.Position, out var lootCoords))
            {
                WriteText(
                    $"{lootItem.GetFormattedValueShortName()}{Environment.NewLine}{Math.Round(lootDist, 0)}m",
                    lootCoords.X + 5,
                    lootCoords.Y - 25,
                    "LOOSE_LOOT");
            }
        }

        // Render LootContainer ESP
        private void RenderLootContainerESP(LootContainer lootContainer, Player localPlayer)
        {
            var lootDist = Vector3.Distance(localPlayer.Position, lootContainer.Position);

            if (!isLootContainerOn || lootDist > lootContainerLimit)
                return;

            if (WorldToScreenLootTest(localPlayer, lootContainer.Position, out var lootCoords))
            {
                WriteText(
                    $"{lootContainer.Name}{Environment.NewLine}{Math.Round(lootDist, 0)}m",
                    lootCoords.X + 5,
                    lootCoords.Y - 25,
                   "CONTAINER_LOOT");
            }
        }

        // Render LootCorpse ESP
        private void RenderLootCorpseESP(LootCorpse lootCorpse, Player localPlayer)
        {
            var lootDist = Vector3.Distance(localPlayer.Position, lootCorpse.Position);

            if (!isLootCorpseOn || lootDist > lootCorpseLimit)
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

        // Render QuestItem ESP
        private void RenderQuestItemESP(QuestItem questItem, Player localPlayer)
        {
            // Skip rendering if QuestItem is completed or ESP is off
            if (!isESPOn || !isQuestItemOn || questItem.Complete) return;

            var questDist = Vector3.Distance(localPlayer.Position, questItem.Position);

            // Early exit if the distance is too far
            if (questDist > questItemLimit) return;

            // Convert world position to screen position
            if (WorldToScreenLootTest(localPlayer, questItem.Position, out var questItemCoords))
            {
                // Draw the QuestItem label and distance
                WriteText(
                    $"{questItem.Name}{Environment.NewLine}{Math.Round(questDist, 0)}m",
                    questItemCoords.X + 5,
                    questItemCoords.Y - 25,
                    "QUEST"); // You can use a different color for QuestItems
            }
        }

        private void RenderWorldObjects(Player localPlayer)
        {
            // Initialize lists for storing positions and screen coordinates
            List<Vector3> objectPositions = new List<Vector3>();
            List<Vector3> screenCoords = new List<Vector3>();

            objectPositions.Clear();

            var localPlayerPos = localPlayer.Position;

            #region Gather Exfil ESP Positions
            // Gather Exfil positions
            var exfils = this.Exfils;
            if (exfils is not null)
            {
                foreach (var exfil in exfils)
                {
                    objectPositions.Add(new Vector3(exfil.Position.X, exfil.Position.Y, exfil.Position.Z));
                }
            }
            #endregion
            #region Gather Grenade ESP Positions
            // Gather Grenade positions
            var grenades = this.Grenades;
            if (grenades is not null)
            {
                foreach (var grenade in grenades)
                {
                    objectPositions.Add(new Vector3(grenade.Position.X, grenade.Position.Y, grenade.Position.Z));
                }
            }
            #endregion
            #region Gather Tripwire ESP Positions
            // Gather Tripwire positions
            var tripwires = this.Tripwires;
            if (tripwires is not null)
            {
                foreach (var tripwire in tripwires)
                {
                    // Add both positions to the objectPositions list
                    objectPositions.Add(new Vector3(tripwire.FromPos.X, tripwire.FromPos.Y, tripwire.FromPos.Z));
                    objectPositions.Add(new Vector3(tripwire.ToPos.X, tripwire.ToPos.Y, tripwire.ToPos.Z));
                }
            }
            #endregion

            // Call WorldToScreenCombined once for all objects
            WorldToScreenCombined(localPlayer, objectPositions, screenCoords);

            // Track index for screen coordinates
            int screenIndex = 0;

            #region Exfil ESP
            // Process Exfil ESP
            if (exfils is not null)
            {
                foreach (var exfil in exfils)
                {
                    var exfilCoords = screenCoords[screenIndex++]; // Use the current screen coordinate
                    var exfilDist = Vector3.Distance(localPlayerPos, exfil.Position);

                    if (exfilCoords.X > 0 || exfilCoords.Y > 0 || exfilCoords.Z > 0)
                    {
                        WriteText(exfil.Name, exfilCoords.X + 5, exfilCoords.Y - 25, "GREEN", 13, "Tarkov-Regular");
                    }
                }
            }
            #endregion
            #region Grenade ESP
            // Process Grenade ESP
            if (grenades is not null)
            {
                foreach (var grenade in grenades)
                {
                    var grenadeCoords = screenCoords[screenIndex++]; // Use the current screen coordinate
                    var grenadeDist = Vector3.Distance(localPlayerPos, grenade.Position);

                    if (grenadeCoords.X > 0 || grenadeCoords.Y > 0 || grenadeCoords.Z > 0)
                    {                    
                        WriteText("Grenade", grenadeCoords.X + 5, grenadeCoords.Y - 25, "RED", 13, "Tarkov-Regular");
                    }
                }
            }
            #endregion
            #region Tripwire ESP
            // Process Tripwire ESP
            if (tripwires is not null)
            {
                var brush = _brushManager.GetBrush("RED");
                foreach (var tripwire in tripwires)
                {
                    // Get screen coordinates for both ends of the tripwire
                    var fromCoords = screenCoords[screenIndex++]; // First position
                    var toCoords = screenCoords[screenIndex++];   // Second position

                    if (fromCoords.X > 0 && fromCoords.Y > 0 && toCoords.X > 0 && toCoords.Y > 0)
                    {
                        // Draw a line between the two points
                        _device.DrawLine(new RawVector2(fromCoords.X, fromCoords.Y), new RawVector2(toCoords.X, toCoords.Y), brush);

                        // Optionally, write text or additional details near the tripwire
                        WriteText("Tripwire", (fromCoords.X + toCoords.X) / 2, (fromCoords.Y + toCoords.Y) / 2 - 25, "WHITE", 13, "Tarkov-Regular");
                    }
                }
            }
            #endregion
        }

        private void DrawSkeletonLines(List<Vector3> coords, string brushName)
        {
            // Use white as default if no color specified
            var drawColor = _brushManager.GetBrush(brushName);

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
                    drawColor, 2.0f
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

        #region Declaration
        private Config _config;
        private int AimFOV
        {
            get => _config.AimbotFOV;
            set => _config.AimbotFOV = value;
        }
        internal struct Margins
        {
            public int Left, Right, Top, Bottom;
        }

        private Margins marg;
        public List<QuestItem> QuestItems { get; set; } = new List<QuestItem>();
        private ReadOnlyDictionary<string, Player> AllPlayers => Memory.Players;

        /// <summary>
        ///     Radar has found Escape From Tarkov process and is ready.
        /// </summary>
        private bool Ready => Memory.Ready;

        /// <summary>
        ///     Radar has found Local Game World.
        /// </summary>
        private bool InGame => Memory.InGame;

        /// <summary>
        ///     LocalPlayer (who is running Radar) 'Player' object.
        /// </summary>
        private Player LocalPlayer => Memory.Players?.FirstOrDefault(x => x.Value.Type is PlayerType.LocalPlayer).Value;

        [DllImport("dwmapi.dll")]
        private static extern void DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins pMargins);
        private BrushManager _brushManager;
        private static WindowRenderTarget _device;
        private HwndRenderTargetProperties _renderProperties;
        

        public static bool ingame = false;

        // Fonts
        private readonly FontFactory _fontFactory = new();

        private Thread _threadDx;

        private CancellationTokenSource _tokenSource;
        private CancellationToken _token;
        private Factory _factory;

        private bool _running;
        #endregion

        #region Start

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

            _tokenSource = new CancellationTokenSource();
            _token = _tokenSource.Token;
            _running = true;
            TopMost = true;

            _threadDx = new Thread(DirectXThread)
            {
                Priority = ThreadPriority.Highest,
                IsBackground = true
            };
            _threadDx.Start();
        }

        private void DisposeOverlayResources()
        {
            if (_threadDx != null && _threadDx.IsAlive)
            {
                _running = false; // Stop the thread
                _tokenSource?.Cancel(); // Signal cancellation
                _threadDx.Join(); // Wait for the thread to finish
            }

            // Dispose of DirectX resources
            _device?.Dispose();
            _factory?.Dispose();
            _brushManager?.Dispose();

            _brushManager = null;
            _device = null;
            _factory = null;
            _tokenSource = null;
            _threadDx = null;
        }

        private void InitializeDirectXResources()
        {
            _factory = new Factory();

            var renderProperties = new HwndRenderTargetProperties
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
                renderProperties
            );

            _brushManager = new BrushManager(_device);
        }
        #endregion

        #region DrawFunctions
        private void WriteText(string msg, float x, float y, string brushName, float fontSize = 13,
            string fontFamily = "Arial Unicode MS")
        {
            var brush = _brushManager.GetBrush(brushName);
            var measure = PredictSize(msg, fontSize, fontFamily);
            _device.DrawText(msg, new TextFormat(_fontFactory, fontFamily, fontSize),
                new RawRectangleF(x, y, x + measure.Width, y + measure.Height), brush);
        }

        private void WriteTextExact(string msg, float x, float y, string brushName, float fontSize = 13,
            string fontFamily = "Arial Unicode MS")
        {
            var brush = _brushManager.GetBrush(brushName);
            var measure = PredictSize(msg, fontSize, fontFamily);
            _device.DrawText(msg, new TextFormat(_fontFactory, fontFamily, fontSize),
                new RawRectangleF(x, y, x + measure.Width, y + measure.Height), brush);
        }

        private void WriteCenterText(string msg, float y, string brushName, float fontSize = 13,
            string fontFamily = "Arial Unicode MS")
        {
            var measure = PredictSize(msg, fontSize, fontFamily);
            var x = Width / 2 - measure.Width / 2;
            WriteText(msg, x, y, brushName, fontSize, fontFamily);
        }

        private void WriteTopLeftText(string msg, string brushName, float fontSize = 13,
        string fontFamily = "Arial Unicode MS", float xOffset = 10, float yOffset = 10)
        {
            // xOffset sets the distance from the left side (default is 10 pixels)
            var x = xOffset;
            // yOffset sets the distance from the top side (default is 10 pixels)
            var y = yOffset;

            // Write text at the calculated position
            WriteText(msg, x, y, brushName, fontSize, fontFamily);
        }

        private void WriteTopRightText(string msg, string brushName, float fontSize = 13,
       string fontFamily = "Arial Unicode MS", float xOffset = 10, float yOffset = 10)
        {
            // yOffset sets the distance from the top side (default is 10 pixels)
            var y = yOffset;

            // Measure the size of the text to determine x position for right alignment
            var measure = PredictSize(msg, fontSize, fontFamily);
            var x = this.Width - measure.Width - xOffset; // Aligns the right side of the text

            // Write text at the calculated position
            WriteText(msg, x, y, brushName, fontSize, fontFamily);
        }

        private void WriteBottomText(string msg, float x, string brushName, float fontSize = 13,
            string fontFamily = "Arial Unicode MS")
        {
            var measure = PredictSize(msg, fontSize, fontFamily);
            var y = Height - measure.Height;
            WriteTextExact(msg, x, y, brushName, fontSize, fontFamily);
        }

        private Size PredictSize(string msg, float fontSize = 13, string fontFamily = "Arial Unicode MS")
        {
            return TextRenderer.MeasureText(msg, new Font(fontFamily, fontSize - 3));
        }
        #endregion

        #region Quit
        private void ClosedOverlay(object sender, FormClosingEventArgs e)
        {
            try
            {
                _running = false;

                _device.Flush();
                _device.EndDraw();
                _factory.Dispose();
                _device.Dispose();
                _device = null;
            }
            catch
            {
            }
        }
        #endregion

        #region Functions
        private bool WorldToScreenCombined(Player player, List<Vector3> enemyPositions, List<Vector3> screenCoords)
        {
            screenCoords.Clear(); // Clear previous results
                                 
            int width = Screen.PrimaryScreen.Bounds.Width;
            int height = Screen.PrimaryScreen.Bounds.Height;

            var temp = Matrix4x4.Transpose(Memory.CameraManager.ViewMatrix);
            var translationVector = new Vector3(temp.M41, temp.M42, temp.M43);
            var up = new Vector3(temp.M21, temp.M22, temp.M23);
            var right = new Vector3(temp.M11, temp.M12, temp.M13);

            foreach (var _Enemy in enemyPositions)
            {
                var w = D3DXVec3Dot(translationVector, _Enemy) + temp.M44;
                if (w < 0.098f)
                {
                    // Skip points behind the camera
                    screenCoords.Add(new Vector3(0, 0, 0)); // Or choose an appropriate default or skip
                    continue;
                }

                var y = D3DXVec3Dot(up, _Enemy) + temp.M24;
                var x = D3DXVec3Dot(right, _Enemy) + temp.M14;
                var screenX = width / 2 * (1f + x / w);
                var screenY = height / 2 * (1f - y / w);

                // Add the calculated screen coordinates
                screenCoords.Add(new Vector3(screenX, screenY, w));
            }
            return true;
        }

        private bool WorldToScreenLootTest(Player player, System.Numerics.Vector3 _Item, out System.Numerics.Vector2 _Screen)
        {
            _Screen = new System.Numerics.Vector2(0, 0);

            // Get the primary screen dimensions
            int width = Screen.PrimaryScreen.Bounds.Width;
            int height = Screen.PrimaryScreen.Bounds.Height;

            var temp = Matrix4x4.Transpose(Memory.CameraManager.ViewMatrix);
            System.Numerics.Vector3 translationVector = new System.Numerics.Vector3(temp.M41, temp.M42, temp.M43);
            System.Numerics.Vector3 up = new System.Numerics.Vector3(temp.M21, temp.M22, temp.M23);
            System.Numerics.Vector3 right = new System.Numerics.Vector3(temp.M11, temp.M12, temp.M13);

            float w = D3DXVec3Dot(translationVector, _Item) + temp.M44;

            // Early return if behind camera
            if (w < 0.098f)
                return false;

            float y = D3DXVec3Dot(up, _Item) + temp.M24;
            float x = D3DXVec3Dot(right, _Item) + temp.M14;

            _Screen.X = (width / 2) * (1f + x / w);
            _Screen.Y = (height / 2) * (1f - y / w);

            return true;
        }

        private float D3DXVec3Dot(Vector3 v1, Vector3 v2)
        {
            return v1.X * v2.X + v1.Y * v2.Y + v1.Z * v2.Z;
        }

        private void CloseOverlay()
        {
            Hide();
            frmMain.isOverlayShown = false;
        }
        #endregion
    }
}