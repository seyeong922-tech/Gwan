using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace EvolvingDesktopPet
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (Array.IndexOf(args, "--self-test") >= 0)
            {
                SelfTest();
                return;
            }
            Application.Run(new PetForm(args));
        }

        private static void SelfTest()
        {
            for (int stage = 1; stage <= 3; stage++)
            {
                string[] suffixes = { "", "-walk", "-talk", "-surprised", "-contempt", "-angry", "-cheer", "-happy", "-pushup", "-paper", "-sleep" };
                foreach (string suffix in suffixes)
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "stage" + stage + suffix + ".png");
                    if (!File.Exists(path)) throw new FileNotFoundException(path);
                    using (Image image = Image.FromFile(path))
                    {
                        if ((image.PixelFormat & PixelFormat.Alpha) == 0) throw new Exception(path + " has no alpha channel");
                        using (Bitmap bitmap = new Bitmap(image))
                        {
                            if (bitmap.GetPixel(0, 0).A != 0) throw new Exception(path + " has a non-transparent corner");
                            if (bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A == 0) throw new Exception(path + " has no opaque subject at center");
                        }
                    }
                }
            }
            string[] branchKeys = { "exercise", "study", "food", "sports", "gaming" };
            string[] branchPoses = { "idle", "walk", "talk", "surprised", "contempt", "angry", "cheer", "happy", "pushup", "paper", "sleep" };
            foreach (string key in branchKeys)
            {
                foreach (string pose in branchPoses)
                {
                    string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "stage2-branches", key, pose + ".png");
                    if (!File.Exists(path)) throw new FileNotFoundException(path);
                    using (Bitmap bitmap = new Bitmap(path))
                    {
                        if ((bitmap.PixelFormat & PixelFormat.Alpha) == 0 || bitmap.GetPixel(0, 0).A != 0)
                            throw new Exception(path + " is not a transparent branch sprite");
                    }
                }
            }
            string[] motionFiles = { "struggle1", "struggle2", "dance1", "dance2" };
            foreach (int stage in new int[] { 1, 3 })
                foreach (string pose in motionFiles)
                    AssertTransparentPng(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "motions", "stage" + stage, pose + ".png"));
            foreach (string key in branchKeys)
            {
                foreach (string pose in motionFiles)
                    AssertTransparentPng(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "stage2-branches", key, pose + ".png"));
            }
            AssertTransparentPng(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "stage2-branches", "gaming", "shh.png"));
            string[] items = { "running_shoe", "pushup_bars", "research_paper", "linkedin", "chicken", "pork", "tomato", "lettuce", "kt_wiz", "arsenal", "tft", "minecraft" };
            foreach (string item in items) AssertTransparentPng(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "items", item + ".png"));
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            JavaScriptSerializer parser = new JavaScriptSerializer();
            Dictionary<string, object> settings = parser.Deserialize<Dictionary<string, object>>(File.ReadAllText(settingsPath, Encoding.UTF8));
            if (!settings.ContainsKey("stage2_stat_threshold") || !settings.ContainsKey("stage3_each_stat_threshold"))
                throw new Exception("stat evolution thresholds are missing");
            string dialoguesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dialogues.json");
            Dictionary<string, string[]> dialogues = parser.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(dialoguesPath, Encoding.UTF8));
            for (int stage = 1; stage <= 3; stage++)
                if (!dialogues.ContainsKey(stage.ToString()) || dialogues[stage.ToString()].Length < 3)
                    throw new Exception("stage " + stage + " dialogue list is too short");
            foreach (string key in branchKeys)
                if (!dialogues.ContainsKey("2_" + key) || dialogues["2_" + key].Length < 5)
                    throw new Exception("stage 2 " + key + " dialogue list is too short");
            string musicPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "music.json");
            Dictionary<string, MusicTrack[]> music = parser.Deserialize<Dictionary<string, MusicTrack[]>>(File.ReadAllText(musicPath, Encoding.UTF8));
            string[] musicKeys = { "1", "2_exercise", "2_study", "2_food", "2_sports", "2_gaming", "3" };
            HashSet<string> videoIds = new HashSet<string>();
            foreach (string key in musicKeys)
            {
                if (!music.ContainsKey(key) || music[key].Length < 3) throw new Exception(key + " music pool is too short");
                foreach (MusicTrack track in music[key])
                    if (track == null || String.IsNullOrEmpty(track.title) || String.IsNullOrEmpty(track.video_id) || !videoIds.Add(track.video_id))
                        throw new Exception("invalid or duplicated music entry in " + key);
            }
            Console.WriteLine("SELF-TEST OK: 88 core/branch sprites, 29 new motion sprites, 12 item icons, evolution settings, dialogue lists, and " + videoIds.Count + " curated tracks");
        }

        private static void AssertTransparentPng(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(path);
            using (Bitmap bitmap = new Bitmap(path))
                if ((bitmap.PixelFormat & PixelFormat.Alpha) == 0 || bitmap.GetPixel(0, 0).A != 0)
                    throw new Exception(path + " is not a transparent PNG");
        }
    }

    internal sealed class PetForm : Form
    {
        private const string AppName = "EvolvingDesktopPet";
        internal static readonly string[] StatKeys = { "exercise", "study", "food", "sports", "gaming" };
        internal static readonly string[] StatLabels = { "운동", "공부", "요리·음식", "축구·야구 시청", "게임" };
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly Timer motionTimer = new Timer();
        private readonly Timer noteTimer = new Timer();
        private readonly Timer evolutionTimer = new Timer();
        private readonly Timer itemTimer = new Timer();
        private readonly Random random = new Random();
        private readonly Stopwatch session = Stopwatch.StartNew();
        private readonly Dictionary<string, Bitmap> rightSprites = new Dictionary<string, Bitmap>();
        private readonly Dictionary<string, Bitmap> leftSprites = new Dictionary<string, Bitmap>();
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem characterMenu = new ToolStripMenuItem("캐릭터 변경");
        private readonly ToolStripMenuItem dropFilterMenu = new ToolStripMenuItem("오브젝트 드롭 방향");
        private readonly string statePath;
        private readonly string settingsPath;
        private readonly string dialoguesPath;
        private readonly string musicPath;

        private Dictionary<string, object> settings;
        private Dictionary<string, object> state;
        private Dictionary<int, List<string>> dialogues;
        private Dictionary<string, List<string>> branchDialogues;
        private Dictionary<string, MusicTrack[]> musicPools;
        private Bitmap currentSprite;
        private Rectangle workArea;
        private string behavior = "idle";
        private DateTime behaviorUntil = DateTime.Now.AddSeconds(3);
        private DateTime lastMotion = DateTime.Now;
        private DateTime lastSave = DateTime.Now;
        private DateTime lastPet = DateTime.MinValue;
        private DateTime nextTalk;
        private DateTime nextItem;
        private Point dragStart;
        private Point formStart;
        private bool dragging;
        private bool movedDuringDrag;
        private bool paused;
        private bool evolving;
        private int evolutionTarget;
        private int evolutionFrame;
        private bool evolutionSilhouette;
        private Bitmap silhouetteSprite;
        private Bitmap nextSilhouetteSprite;
        private string pendingEvolutionPath = "";
        private int stage;
        private int direction = -1;
        private string currentPose = "idle";
        private int spriteHeight;
        private double walkSpeed;
        private string lastSpawnedItemKind = "";
        private double baseY;
        private string note = "";
        private string sleepText = "";
        private string speech = "";
        private InteractionItemForm activeItem;
        private MusicTrack recommendedTrack;
        private bool playMusicAfterEvolution;

        public PetForm(string[] args)
        {
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (String.IsNullOrEmpty(local)) local = Path.GetTempPath();
            string dataDirectory = Environment.GetEnvironmentVariable("EVOPET_DATA_DIR");
            if (String.IsNullOrEmpty(dataDirectory)) dataDirectory = Path.Combine(local, AppName);
            Directory.CreateDirectory(dataDirectory);
            statePath = Path.Combine(dataDirectory, "state.json");
            settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            dialoguesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dialogues.json");
            musicPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "music.json");

            settings = LoadSettings();
            state = LoadState();
            dialogues = LoadDialogues();
            musicPools = LoadMusic();
            stage = Clamp(ToInt(Get(state, "stage", 1)), 1, 3);
            spriteHeight = Math.Max(120, ToInt(Get(settings, "sprite_height", 230)));
            walkSpeed = Math.Max(0.2, ToDouble(Get(settings, "walk_speed", 2.1)));

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(1, 2, 3);
            TransparencyKey = BackColor;
            TopMost = ToBool(Get(settings, "always_on_top", true));
            StartPosition = FormStartPosition.Manual;
            Width = Math.Max(380, (int)(spriteHeight * 1.45));
            Height = Math.Max(350, (int)(spriteHeight * 1.48));
            workArea = Screen.PrimaryScreen.WorkingArea;
            int savedX = ToInt(Get(state, "x", workArea.Right - Width - 36));
            if (savedX < workArea.Left) savedX = workArea.Right - Width - 36;
            Left = Clamp(savedX, workArea.Left, workArea.Right - Width);
            Top = workArea.Bottom - Height;
            baseY = Top;

            LoadSprites();
            SelectSprite();
            BuildMenu();
            ScheduleNextTalk(6, 12);
            ScheduleNextItem(8, 14);

            MouseDown += OnPetMouseDown;
            MouseMove += OnPetMouseMove;
            MouseUp += OnPetMouseUp;
            MouseLeave += delegate { if (!dragging) Cursor = Cursors.Default; };
            Paint += OnPetPaint;
            FormClosing += OnPetClosing;

            motionTimer.Interval = 40;
            motionTimer.Tick += MotionTick;
            motionTimer.Start();
            noteTimer.Interval = 1100;
            noteTimer.Tick += delegate { noteTimer.Stop(); note = ""; Invalidate(); };
            evolutionTimer.Interval = 120;
            evolutionTimer.Tick += EvolutionTick;
            itemTimer.Interval = 1000;
            itemTimer.Tick += ItemTick;
            itemTimer.Start();

            bool qaEnabled = Environment.GetEnvironmentVariable("EVOPET_ENABLE_QA") == "1";
            if (!qaEnabled) args = new string[0];

            if (Array.IndexOf(args, "--qa-music") >= 0)
            {
                Timer musicQaTimer = new Timer();
                musicQaTimer.Interval = 500;
                musicQaTimer.Tick += delegate
                {
                    musicQaTimer.Stop();
                    MusicTrack[] qaPool;
                    if (musicPools.TryGetValue("1", out qaPool) && qaPool.Length > 0)
                        ShowMusicRecommendation(qaPool[0]);
                };
                musicQaTimer.Start();
            }

            if (Array.IndexOf(args, "--qa-spawn-item") >= 0)
            {
                Timer spawnQaTimer = new Timer();
                spawnQaTimer.Interval = 500;
                spawnQaTimer.Tick += delegate { spawnQaTimer.Stop(); SpawnItem(); };
                spawnQaTimer.Start();
            }

            int itemQaIndex = Array.IndexOf(args, "--qa-spawn-items");
            if (itemQaIndex >= 0 && itemQaIndex + 2 < args.Length)
            {
                int qaItemCount;
                string qaItemPath = args[itemQaIndex + 2];
                if (Int32.TryParse(args[itemQaIndex + 1], out qaItemCount))
                {
                    Timer itemQaTimer = new Timer();
                    itemQaTimer.Interval = 140;
                    int spawned = 0;
                    List<string> observed = new List<string>();
                    itemQaTimer.Tick += delegate
                    {
                        if (activeItem != null && !activeItem.IsDisposed) activeItem.Close();
                        SpawnItem();
                        observed.Add(lastSpawnedItemKind);
                        spawned++;
                        if (spawned >= qaItemCount)
                        {
                            itemQaTimer.Stop();
                            File.WriteAllLines(qaItemPath, observed.ToArray(), Encoding.UTF8);
                        }
                    };
                    itemQaTimer.Start();
                }
            }

            int preferenceQaIndex = Array.IndexOf(args, "--qa-drop-filter");
            if (preferenceQaIndex < 0) preferenceQaIndex = Array.IndexOf(args, "--qa-preference");
            if (preferenceQaIndex >= 0 && preferenceQaIndex + 1 < args.Length)
            {
                string qaPreference = args[preferenceQaIndex + 1];
                if (qaPreference == "auto" || Array.IndexOf(StatKeys, qaPreference) >= 0)
                    state["drop_filter"] = qaPreference;
            }

            int switchQaIndex = Array.IndexOf(args, "--qa-switch-character");
            if (switchQaIndex >= 0 && switchQaIndex + 1 < args.Length)
            {
                string qaCharacter = args[switchQaIndex + 1];
                Timer switchQaTimer = new Timer();
                switchQaTimer.Interval = 350;
                switchQaTimer.Tick += delegate
                {
                    switchQaTimer.Stop();
                    if (qaCharacter == "1") SwitchCharacter(1, "");
                    else if (qaCharacter == "3") SwitchCharacter(3, "");
                    else if (Array.IndexOf(StatKeys, qaCharacter) >= 0) SwitchCharacter(2, qaCharacter);
                };
                switchQaTimer.Start();
            }

            if (Array.IndexOf(args, "--qa-interactions") >= 0)
            {
                Timer interactionQaTimer = new Timer();
                interactionQaTimer.Interval = 350;
                interactionQaTimer.Tick += delegate
                {
                    interactionQaTimer.Stop();
                    AcceptItem("running_shoe");
                    AcceptItem("research_paper");
                    AcceptItem("chicken");
                    AcceptItem("arsenal");
                    AcceptItem("minecraft");
                };
                interactionQaTimer.Start();
            }

            int evolutionQaIndex = Array.IndexOf(args, "--qa-evolution");
            if (evolutionQaIndex >= 0 && evolutionQaIndex + 2 < args.Length)
            {
                string qaPath = args[evolutionQaIndex + 1];
                int qaValue;
                if (Array.IndexOf(StatKeys, qaPath) >= 0 && Int32.TryParse(args[evolutionQaIndex + 2], out qaValue))
                {
                    Dictionary<string, object> qaStats = EnsureNestedState("stats", EmptyStats());
                    qaStats[qaPath] = qaValue;
                    state["last_stat"] = qaPath;
                    CheckEvolution();
                }
            }

            if (Array.IndexOf(args, "--qa-final-evolution") >= 0)
            {
                Dictionary<string, object> qaStats = EnsureNestedState("stats", EmptyStats());
                int threshold = Math.Max(1, ToInt(Get(settings, "stage3_each_stat_threshold", 20)));
                foreach (string key in StatKeys) qaStats[key] = threshold;
                CheckEvolution();
            }

            if (Array.IndexOf(args, "--qa-lettuce") >= 0)
            {
                Timer lettuceQaTimer = new Timer();
                lettuceQaTimer.Interval = 350;
                lettuceQaTimer.Tick += delegate
                {
                    lettuceQaTimer.Stop();
                    Dictionary<string, object> qaStats = EnsureNestedState("stats", EmptyStats());
                    foreach (string key in StatKeys) qaStats[key] = 2;
                    AcceptItem("lettuce");
                };
                lettuceQaTimer.Start();
            }

            int chartQaIndex = Array.IndexOf(args, "--qa-chart");
            if (chartQaIndex >= 0 && chartQaIndex + 1 < args.Length)
            {
                string chartPath = args[chartQaIndex + 1];
                Timer chartQaTimer = new Timer();
                chartQaTimer.Interval = 300;
                chartQaTimer.Tick += delegate
                {
                    chartQaTimer.Stop();
                    using (StatChartForm chart = new StatChartForm(
                        CurrentStageName(), stage, Convert.ToString(Get(state, "stage2_path", "")),
                        GetDropFilter(),
                        EnsureNestedState("stats", EmptyStats()),
                        ToInt(Get(settings, "stage2_stat_threshold", 10)),
                        ToInt(Get(settings, "stage3_each_stat_threshold", 20))))
                    using (Bitmap preview = new Bitmap(chart.ClientSize.Width, chart.ClientSize.Height))
                    {
                        chart.DrawToBitmap(preview, new Rectangle(Point.Empty, chart.ClientSize));
                        preview.Save(chartPath, ImageFormat.Png);
                    }
                };
                chartQaTimer.Start();
            }

            int poseQaIndex = Array.IndexOf(args, "--qa-pose");
            if (poseQaIndex >= 0 && poseQaIndex + 1 < args.Length && !evolving)
            {
                paused = true;
                SetPose(args[poseQaIndex + 1]);
            }

            int screenshotQaIndex = Array.IndexOf(args, "--qa-screenshot");
            if (screenshotQaIndex >= 0 && screenshotQaIndex + 1 < args.Length)
            {
                string screenshotPath = args[screenshotQaIndex + 1];
                int delay = 500;
                if (screenshotQaIndex + 2 < args.Length) Int32.TryParse(args[screenshotQaIndex + 2], out delay);
                Timer screenshotQaTimer = new Timer();
                screenshotQaTimer.Interval = Math.Max(100, delay);
                screenshotQaTimer.Tick += delegate
                {
                    screenshotQaTimer.Stop();
                    using (Bitmap preview = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
                    {
                        DrawToBitmap(preview, new Rectangle(Point.Empty, ClientSize));
                        preview.Save(screenshotPath, ImageFormat.Png);
                    }
                };
                screenshotQaTimer.Start();
            }

            int exitIndex = Array.IndexOf(args, "--qa-exit-ms");
            if (exitIndex >= 0 && exitIndex + 1 < args.Length)
            {
                int milliseconds;
                if (Int32.TryParse(args[exitIndex + 1], out milliseconds))
                {
                    Timer qaTimer = new Timer();
                    qaTimer.Interval = Math.Max(100, milliseconds);
                    qaTimer.Tick += delegate { qaTimer.Stop(); Close(); };
                    qaTimer.Start();
                }
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        private Dictionary<string, object> LoadSettings()
        {
            Dictionary<string, object> defaults = new Dictionary<string, object>();
            defaults["pet_name"] = "진화펫";
            defaults["sprite_height"] = 230;
            defaults["walk_speed"] = 2.1;
            defaults["feed_cooldown_seconds"] = 60;
            defaults["always_on_top"] = true;
            defaults["talk_interval_min_seconds"] = 18;
            defaults["talk_interval_max_seconds"] = 32;
            defaults["item_interval_min_seconds"] = 25;
            defaults["item_interval_max_seconds"] = 45;
            defaults["item_lifetime_seconds"] = 20;
            defaults["stage2_stat_threshold"] = 10;
            defaults["stage3_each_stat_threshold"] = 20;
            try
            {
                if (File.Exists(settingsPath))
                {
                    Dictionary<string, object> loaded = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(settingsPath, Encoding.UTF8));
                    foreach (KeyValuePair<string, object> pair in loaded) defaults[pair.Key] = pair.Value;
                }
            }
            catch { }
            return defaults;
        }

        private Dictionary<string, object> LoadState()
        {
            Dictionary<string, object> result = DefaultState();
            try
            {
                if (File.Exists(statePath))
                {
                    Dictionary<string, object> loaded = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath, Encoding.UTF8));
                    foreach (KeyValuePair<string, object> pair in loaded) result[pair.Key] = pair.Value;
                }
            }
            catch { }
            MigrateState(result);
            return result;
        }

        private static void MigrateState(Dictionary<string, object> result)
        {
            Dictionary<string, object> existingStats = AsDictionary(Get(result, "stats", null));
            if (!existingStats.ContainsKey("exercise"))
            {
                result["legacy_stats"] = existingStats;
                result["stats"] = Dict(
                    "exercise", ToInt(Get(existingStats, "stamina", 0)),
                    "study", ToInt(Get(existingStats, "research", 0)),
                    "food", 0,
                    "sports", ToInt(Get(existingStats, "fandom", 0)),
                    "gaming", 0);
            }
            Dictionary<string, object> existingCounts = AsDictionary(Get(result, "item_counts", null));
            if (!existingCounts.ContainsKey("running_shoe"))
            {
                result["legacy_item_counts"] = existingCounts;
                Dictionary<string, object> migrated = EmptyItemCounts();
                migrated["pushup_bars"] = ToInt(Get(existingCounts, "dumbbell", Get(existingCounts, "energy", 0)));
                migrated["research_paper"] = ToInt(Get(existingCounts, "book", Get(existingCounts, "paper", 0)));
                migrated["chicken"] = ToInt(Get(existingCounts, "meal", 0));
                migrated["arsenal"] = ToInt(Get(existingCounts, "sports", Get(existingCounts, "football", 0)));
                migrated["minecraft"] = ToInt(Get(existingCounts, "gamepad", 0));
                result["item_counts"] = migrated;
            }
            if (!result.ContainsKey("last_stat")) result["last_stat"] = "";
            if (!result.ContainsKey("stage2_path")) result["stage2_path"] = "";
            if (!result.ContainsKey("stage2_evolution_seen")) result["stage2_evolution_seen"] = ToInt(Get(result, "stage", 1)) >= 2;
            if (!result.ContainsKey("final_unlocked")) result["final_unlocked"] = ToInt(Get(result, "stage", 1)) >= 3;
            Dictionary<string, object> unlockedPaths = AsDictionary(Get(result, "unlocked_paths", null));
            if (unlockedPaths.Count == 0)
            {
                unlockedPaths = EmptyUnlockedPaths();
                Dictionary<string, object> stats = AsDictionary(Get(result, "stats", null));
                foreach (string key in StatKeys) unlockedPaths[key] = ToInt(Get(stats, key, 0)) >= 10;
                string currentPath = Convert.ToString(Get(result, "stage2_path", ""));
                if (Array.IndexOf(StatKeys, currentPath) >= 0) unlockedPaths[currentPath] = true;
                result["unlocked_paths"] = unlockedPaths;
            }
            if (!result.ContainsKey("music_enabled")) result["music_enabled"] = true;
            if (!result.ContainsKey("last_music_video")) result["last_music_video"] = "";
            string dropFilter = Convert.ToString(Get(result, "drop_filter", Get(result, "preferred_path", "auto")));
            if (dropFilter != "auto" && Array.IndexOf(StatKeys, dropFilter) < 0) dropFilter = "auto";
            result["drop_filter"] = dropFilter;
        }

        private Dictionary<int, List<string>> LoadDialogues()
        {
            Dictionary<int, List<string>> result = new Dictionary<int, List<string>>();
            branchDialogues = new Dictionary<string, List<string>>();
            result[1] = new List<string> { "오늘의 추천은 역시 삼선이지.", "편안함과 스타일, 둘 다 잡아야지." };
            result[2] = new List<string> { "COYG! 오늘도 아스날이다!", "북런던은 빨갛다!" };
            result[3] = new List<string> { "삼선 장착, COYG 준비 완료!", "광고도 응원도 내가 하면 다르지." };
            try
            {
                if (File.Exists(dialoguesPath))
                {
                    Dictionary<string, string[]> loaded = json.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(dialoguesPath, Encoding.UTF8));
                    for (int number = 1; number <= 3; number++)
                    {
                        string[] lines;
                        if (loaded.TryGetValue(number.ToString(), out lines) && lines != null && lines.Length > 0)
                            result[number] = new List<string>(lines);
                    }
                    foreach (string key in StatKeys)
                    {
                        string[] lines;
                        if (loaded.TryGetValue("2_" + key, out lines) && lines != null && lines.Length > 0)
                            branchDialogues[key] = new List<string>(lines);
                    }
                }
            }
            catch { }
            return result;
        }

        private Dictionary<string, MusicTrack[]> LoadMusic()
        {
            try
            {
                Dictionary<string, MusicTrack[]> loaded = json.Deserialize<Dictionary<string, MusicTrack[]>>(File.ReadAllText(musicPath, Encoding.UTF8));
                return loaded ?? new Dictionary<string, MusicTrack[]>();
            }
            catch { return new Dictionary<string, MusicTrack[]>(); }
        }

        private static Dictionary<string, object> DefaultState()
        {
            return Dict(
                "stage", 1, "pet_count", 0, "feed_count", 0, "active_seconds", 0.0,
                "last_feed_epoch", 0.0, "x", -1,
                "last_stat", "", "stage2_path", "", "drop_filter", "auto", "stage2_evolution_seen", false, "final_unlocked", false,
                "music_enabled", true, "last_music_video", "",
                "stats", Dict("exercise", 0, "study", 0, "food", 0, "sports", 0, "gaming", 0),
                "unlocked_paths", EmptyUnlockedPaths(),
                "item_counts", EmptyItemCounts());
        }

        private static Dictionary<string, object> Dict(params object[] values)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            for (int i = 0; i + 1 < values.Length; i += 2) result[Convert.ToString(values[i])] = values[i + 1];
            return result;
        }

        private void LoadSprites()
        {
            string assetDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            for (int number = 1; number <= 3; number++)
            {
                string[] poseNames = { "idle", "walk", "talk", "surprised", "contempt", "angry", "cheer", "happy", "pushup", "paper", "sleep" };
                string[] suffixes = { "", "-walk", "-talk", "-surprised", "-contempt", "-angry", "-cheer", "-happy", "-pushup", "-paper", "-sleep" };
                for (int pose = 0; pose < suffixes.Length; pose++)
                {
                    string path = Path.Combine(assetDirectory, "stage" + number + suffixes[pose] + ".png");
                    using (Bitmap source = new Bitmap(path))
                    {
                        Rectangle crop = FindOpaqueBounds(source);
                        int targetHeight = spriteHeight;
                        if (poseNames[pose] == "walk") targetHeight = (int)Math.Round(spriteHeight * 0.98);
                        if (poseNames[pose] == "pushup" || poseNames[pose] == "paper") targetHeight = (int)Math.Round(spriteHeight * 0.82);
                        if (poseNames[pose] == "sleep") targetHeight = (int)Math.Round(spriteHeight * 0.62);
                        double scale = targetHeight / (double)crop.Height;
                        int width = Math.Max(1, (int)Math.Round(crop.Width * scale));
                        int maxWidth = Width - 20;
                        if (width > maxWidth)
                        {
                            scale *= maxWidth / (double)width;
                            width = maxWidth;
                            targetHeight = Math.Max(1, (int)Math.Round(crop.Height * scale));
                        }
                        Bitmap prepared = new Bitmap(width, targetHeight, PixelFormat.Format32bppArgb);
                        using (Graphics graphics = Graphics.FromImage(prepared))
                        {
                            graphics.Clear(Color.Transparent);
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.DrawImage(source, new Rectangle(0, 0, width, targetHeight), crop, GraphicsUnit.Pixel);
                        }
                        string key = SpriteKey(number, poseNames[pose]);
                        rightSprites[key] = prepared;
                        leftSprites[key] = Flip(prepared);
                    }
                }
            }
            string branchDirectory = Path.Combine(assetDirectory, "stage2-branches");
            string[] branchPoses = { "idle", "walk", "talk", "surprised", "contempt", "angry", "cheer", "happy", "pushup", "paper", "sleep" };
            foreach (string branch in StatKeys)
            {
                foreach (string pose in branchPoses)
                {
                    string path = Path.Combine(branchDirectory, branch, pose + ".png");
                    using (Bitmap source = new Bitmap(path))
                    {
                        Rectangle crop = FindOpaqueBounds(source);
                        int targetHeight = spriteHeight;
                        if (pose == "walk") targetHeight = (int)Math.Round(spriteHeight * 0.98);
                        if (pose == "pushup" || pose == "paper") targetHeight = (int)Math.Round(spriteHeight * 0.82);
                        if (pose == "sleep") targetHeight = (int)Math.Round(spriteHeight * 0.62);
                        double scale = targetHeight / (double)crop.Height;
                        int width = Math.Max(1, (int)Math.Round(crop.Width * scale));
                        int maxWidth = Width - 20;
                        if (width > maxWidth)
                        {
                            scale *= maxWidth / (double)width;
                            width = maxWidth;
                            targetHeight = Math.Max(1, (int)Math.Round(crop.Height * scale));
                        }
                        Bitmap prepared = new Bitmap(width, targetHeight, PixelFormat.Format32bppArgb);
                        using (Graphics graphics = Graphics.FromImage(prepared))
                        {
                            graphics.Clear(Color.Transparent);
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.DrawImage(source, new Rectangle(0, 0, width, targetHeight), crop, GraphicsUnit.Pixel);
                        }
                        string key = BranchSpriteKey(branch, pose);
                        rightSprites[key] = prepared;
                        leftSprites[key] = Flip(prepared);
                    }
                }
            }
            string[] motionPoses = { "struggle1", "struggle2", "dance1", "dance2" };
            foreach (int number in new int[] { 1, 3 })
            {
                foreach (string pose in motionPoses)
                    LoadMotionSprite(Path.Combine(assetDirectory, "motions", "stage" + number, pose + ".png"), SpriteKey(number, pose), pose);
            }
            foreach (string branch in StatKeys)
            {
                foreach (string pose in motionPoses)
                    LoadMotionSprite(Path.Combine(branchDirectory, branch, pose + ".png"), BranchSpriteKey(branch, pose), pose);
            }
            LoadMotionSprite(Path.Combine(branchDirectory, "gaming", "shh.png"), BranchSpriteKey("gaming", "shh"), "shh");
        }

        private void LoadMotionSprite(string path, string key, string pose)
        {
            using (Bitmap source = new Bitmap(path))
            {
                Rectangle crop = FindOpaqueBounds(source);
                int targetHeight = (int)Math.Round(spriteHeight * (pose.StartsWith("struggle") ? 0.92 : 1.0));
                double scale = targetHeight / (double)crop.Height;
                int width = Math.Max(1, (int)Math.Round(crop.Width * scale));
                int maxWidth = Width - 20;
                if (width > maxWidth) { scale *= maxWidth / (double)width; width = maxWidth; targetHeight = Math.Max(1, (int)Math.Round(crop.Height * scale)); }
                Bitmap prepared = new Bitmap(width, targetHeight, PixelFormat.Format32bppArgb);
                using (Graphics graphics = Graphics.FromImage(prepared))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(source, new Rectangle(0, 0, width, targetHeight), crop, GraphicsUnit.Pixel);
                }
                rightSprites[key] = prepared;
                leftSprites[key] = Flip(prepared);
            }
        }

        private static string SpriteKey(int number, string pose)
        {
            return number + ":" + pose;
        }

        private static string BranchSpriteKey(string branch, string pose)
        {
            return "2-" + branch + ":" + pose;
        }

        private static Rectangle FindOpaqueBounds(Bitmap image)
        {
            int left = image.Width, top = image.Height, right = 0, bottom = 0;
            for (int y = 0; y < image.Height; y += 2)
            {
                for (int x = 0; x < image.Width; x += 2)
                {
                    if (image.GetPixel(x, y).A > 32)
                    {
                        left = Math.Min(left, x); top = Math.Min(top, y);
                        right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    }
                }
            }
            if (right <= left || bottom <= top) return new Rectangle(0, 0, image.Width, image.Height);
            return Rectangle.FromLTRB(Math.Max(0, left - 2), Math.Max(0, top - 2), Math.Min(image.Width, right + 3), Math.Min(image.Height, bottom + 3));
        }

        private static Bitmap Flip(Bitmap source)
        {
            Bitmap copy = new Bitmap(source);
            copy.RotateFlip(RotateFlipType.RotateNoneFlipX);
            return copy;
        }

        private void BuildMenu()
        {
            menu.Font = new Font("Segoe UI", 10);
            menu.Items.Add("상태 보기", null, delegate { ShowStatus(); });
            menu.Items.Add("상호작용 아이템 바로 생성", null, delegate { SpawnItem(); });
            menu.Items.Add("대화 목록 다시 불러오기", null, delegate
            {
                dialogues = LoadDialogues();
                ShowNote("대사 갱신!");
                ScheduleNextTalk(2, 5);
            });
            AddDropFilterItem("자동 (모든 종류)", "auto");
            AddDropFilterItem("운동 오브젝트", "exercise");
            AddDropFilterItem("공부 오브젝트", "study");
            AddDropFilterItem("요리·음식 오브젝트", "food");
            AddDropFilterItem("경기 시청 오브젝트", "sports");
            AddDropFilterItem("게임 오브젝트", "gaming");
            menu.Items.Add(dropFilterMenu);
            menu.Items.Add(characterMenu);
            menu.Opening += delegate { RefreshCharacterMenu(); };
            ToolStripMenuItem musicItem = new ToolStripMenuItem("상호작용 음악 추천");
            musicItem.CheckOnClick = true;
            musicItem.Checked = ToBool(Get(state, "music_enabled", true));
            musicItem.CheckedChanged += delegate
            {
                state["music_enabled"] = musicItem.Checked;
                SaveState();
                ShowNote(musicItem.Checked ? "음악 추천 켜짐" : "음악 추천 꺼짐");
            };
            menu.Items.Add(musicItem);
            menu.Items.Add("지금 음악 추천", null, delegate { SuggestMusic(); });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem pauseItem = new ToolStripMenuItem("움직임 잠시 멈춤");
            pauseItem.CheckOnClick = true;
            pauseItem.CheckedChanged += delegate { paused = pauseItem.Checked; };
            menu.Items.Add(pauseItem);
            ToolStripMenuItem topItem = new ToolStripMenuItem("항상 위에 표시");
            topItem.CheckOnClick = true;
            topItem.Checked = TopMost;
            topItem.CheckedChanged += delegate { TopMost = topItem.Checked; };
            menu.Items.Add(topItem);
            ToolStripMenuItem startupItem = new ToolStripMenuItem("Windows 시작 시 실행");
            startupItem.CheckOnClick = true;
            startupItem.Checked = IsStartupEnabled();
            startupItem.CheckedChanged += delegate { ToggleStartup(startupItem.Checked); };
            menu.Items.Add(startupItem);
            menu.Items.Add("진행 기록 초기화", null, delegate { ResetProgress(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { Close(); });
        }

        private void AddDropFilterItem(string label, string value)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Tag = value;
            item.Checked = GetDropFilter() == value;
            item.Click += delegate
            {
                SetDropFilter(value);
                foreach (ToolStripItem child in dropFilterMenu.DropDownItems)
                {
                    ToolStripMenuItem option = child as ToolStripMenuItem;
                    if (option != null) option.Checked = Convert.ToString(option.Tag) == value;
                }
            };
            dropFilterMenu.DropDownItems.Add(item);
        }

        private string GetDropFilter()
        {
            string filter = Convert.ToString(Get(state, "drop_filter", "auto"));
            return filter == "auto" || Array.IndexOf(StatKeys, filter) >= 0 ? filter : "auto";
        }

        private void SetDropFilter(string path)
        {
            if (path != "auto" && Array.IndexOf(StatKeys, path) < 0) return;
            state["drop_filter"] = path;
            SaveState();
            ShowNote(path == "auto" ? "모든 오브젝트 등장" : StatLabel(path) + " 오브젝트만 등장");
        }

        private bool IsFinalUnlocked()
        {
            return ToBool(Get(state, "final_unlocked", false));
        }

        private bool IsBranchUnlocked(string path)
        {
            if (IsFinalUnlocked()) return true;
            Dictionary<string, object> unlocked = EnsureNestedState("unlocked_paths", EmptyUnlockedPaths());
            return Array.IndexOf(StatKeys, path) >= 0 && ToBool(Get(unlocked, path, false));
        }

        private void RefreshCharacterMenu()
        {
            characterMenu.DropDownItems.Clear();
            AddCharacterChoice("관호", 1, "", true);
            AddCharacterChoice("수원고 달리기 1등출신", 2, "exercise", IsBranchUnlocked("exercise"));
            AddCharacterChoice("master in England", 2, "study", IsBranchUnlocked("study"));
            AddCharacterChoice("관주부", 2, "food", IsBranchUnlocked("food"));
            AddCharacterChoice("관계인", 2, "sports", IsBranchUnlocked("sports"));
            AddCharacterChoice("관이커", 2, "gaming", IsBranchUnlocked("gaming"));
            AddCharacterChoice("관종대왕", 3, "", IsFinalUnlocked());
        }

        private void AddCharacterChoice(string label, int targetStage, string path, bool unlocked)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(unlocked ? label : "🔒 " + label);
            item.Enabled = unlocked;
            item.Checked = stage == targetStage && (targetStage != 2 || Convert.ToString(Get(state, "stage2_path", "")) == path);
            item.Click += delegate { SwitchCharacter(targetStage, path); };
            characterMenu.DropDownItems.Add(item);
        }

        private void SwitchCharacter(int targetStage, string path)
        {
            if (evolving) return;
            if (targetStage == 2 && !IsBranchUnlocked(path)) { ShowNote("아직 해금되지 않았습니다."); return; }
            if (targetStage == 3 && !IsFinalUnlocked()) { ShowNote("아직 해금되지 않았습니다."); return; }
            stage = targetStage;
            state["stage"] = stage;
            if (stage == 2) state["stage2_path"] = path;
            currentPose = "idle";
            behavior = "idle";
            behaviorUntil = DateTime.Now.AddSeconds(3);
            speech = "";
            recommendedTrack = null;
            SelectSprite();
            SaveState();
            ShowNote(CurrentStageName() + "으로 변경");
        }

        private void OnPetPaint(object sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            Bitmap paintedSprite = currentSprite;
            if (evolving && evolutionSilhouette)
            {
                Bitmap alternatingSilhouette = evolutionFrame % 2 == 0 ? silhouetteSprite : nextSilhouetteSprite;
                if (alternatingSilhouette != null) paintedSprite = alternatingSilhouette;
            }
            if (paintedSprite != null)
            {
                int x = (Width - paintedSprite.Width) / 2;
                int y = Height - paintedSprite.Height - 4;
                e.Graphics.DrawImageUnscaled(paintedSprite, x, y);
            }

            Color skyBlue = Color.FromArgb(135, 206, 235);
            Color darkNavy = Color.FromArgb(12, 31, 49);
            if (!String.IsNullOrEmpty(speech))
            {
                Rectangle bubble = new Rectangle(10, 8, Width - 20, 76);
                using (GraphicsPath path = RoundedRectangle(bubble, 16))
                using (Brush fill = new SolidBrush(darkNavy))
                using (Pen border = new Pen(skyBlue, 2.5f))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(border, path);
                    Point[] tail = { new Point(Width / 2 - 8, 83), new Point(Width / 2 + 10, 83), new Point(Width / 2, 94) };
                    e.Graphics.FillPolygon(fill, tail);
                    e.Graphics.DrawLines(border, new Point[] { tail[0], tail[2], tail[1] });
                }
                using (Font speechFont = new Font("Malgun Gothic", 12.5f, FontStyle.Bold))
                using (Brush textBrush = new SolidBrush(skyBlue))
                using (StringFormat center = new StringFormat())
                {
                    center.Alignment = StringAlignment.Center;
                    center.LineAlignment = StringAlignment.Center;
                    center.Trimming = StringTrimming.EllipsisCharacter;
                    e.Graphics.DrawString(speech, speechFont, textBrush, new RectangleF(24, 14, Width - 48, 62), center);
                }
                if (recommendedTrack != null)
                {
                    using (Pen underline = new Pen(skyBlue, 1.2f)) e.Graphics.DrawLine(underline, 72, 70, Width - 72, 70);
                }
            }

            using (Font noteFont = new Font("Malgun Gothic", 16, FontStyle.Bold))
            {
                int noteY = String.IsNullOrEmpty(speech) ? 8 : 94;
                DrawOutlinedText(e.Graphics, note, noteFont, new RectangleF(0, noteY, Width, 34), skyBlue, darkNavy);
                DrawOutlinedText(e.Graphics, sleepText, noteFont, new RectangleF(Width - 74, 100, 62, 40), skyBlue, darkNavy);
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawOutlinedText(Graphics graphics, string text, Font font, RectangleF bounds, Color fill, Color outline)
        {
            if (String.IsNullOrEmpty(text)) return;
            using (StringFormat center = new StringFormat())
            using (Brush outlineBrush = new SolidBrush(outline))
            using (Brush fillBrush = new SolidBrush(fill))
            {
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Center;
                for (int x = -2; x <= 2; x++)
                    for (int y = -2; y <= 2; y++)
                        if (x != 0 || y != 0) graphics.DrawString(text, font, outlineBrush, new RectangleF(bounds.X + x, bounds.Y + y, bounds.Width, bounds.Height), center);
                graphics.DrawString(text, font, fillBrush, bounds, center);
            }
        }

        private void MotionTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            double elapsed = Math.Min(0.25, (now - lastMotion).TotalSeconds);
            lastMotion = now;
            state["active_seconds"] = ToDouble(Get(state, "active_seconds", 0.0)) + elapsed;

            if (!paused && !evolving && dragging)
            {
                string struggle = ((int)(session.Elapsed.TotalSeconds / 0.45) % 2 == 0) ? "struggle1" : "struggle2";
                SetPose(struggle);
            }
            else if (!paused && !evolving && !dragging)
            {
                if (now >= nextTalk && behavior != "talk") StartTalking();
                else if (now >= behaviorUntil)
                {
                    speech = "";
                    recommendedTrack = null;
                    ChooseBehavior();
                }
                double seconds = session.Elapsed.TotalSeconds;
                double bob = 0;
                string pose = "idle";
                if (behavior == "wander")
                {
                    Left += (int)Math.Round(direction * walkSpeed);
                    bob = -Math.Abs(Math.Sin(seconds * 5.2)) * 5;
                    int walkFrame = (int)(seconds / 0.55) % 2;
                    pose = walkFrame == 0 ? "idle" : "walk";
                    if (Left <= workArea.Left) { Left = workArea.Left; direction = 1; SelectSprite(); }
                    if (Right >= workArea.Right) { Left = workArea.Right - Width; direction = -1; SelectSprite(); }
                }
                else if (behavior == "idle") bob = Math.Sin(seconds * 2.3) * 2;
                else if (behavior == "stretch") { bob = Math.Sin(seconds * 2.5) * 3; pose = "happy"; }
                else if (behavior == "hop") { bob = -Math.Abs(Math.Sin(seconds * 4.8)) * 16; pose = ((int)(seconds / 0.65) % 2 == 0) ? "walk" : "happy"; }
                else if (behavior == "run")
                {
                    Left += (int)Math.Round(direction * walkSpeed * 2.6);
                    bob = -Math.Abs(Math.Sin(seconds * 8.5)) * 10;
                    pose = ((int)(seconds / 0.65) % 2 == 0) ? "walk" : "cheer";
                    if (Left <= workArea.Left) { Left = workArea.Left; direction = 1; SelectSprite(); }
                    if (Right >= workArea.Right) { Left = workArea.Right - Width; direction = -1; SelectSprite(); }
                }
                else if (behavior == "celebrate") { bob = -Math.Abs(Math.Sin(seconds * 6.2)) * 8; pose = "cheer"; }
                else if (behavior == "talk") { bob = Math.Sin(seconds * 2.8) * 2; pose = "talk"; }
                else if (behavior == "pushup") { bob = Math.Sin(seconds * 6.0) * 3; pose = "pushup"; }
                else if (behavior == "paper") { bob = Math.Sin(seconds * 3.0); pose = "paper"; }
                else if (behavior == "sleep") { bob = Math.Sin(seconds * 1.3); pose = "sleep"; }
                else if (behavior == "dance") { bob = -Math.Abs(Math.Sin(seconds * 3.5)) * 5; pose = ((int)(seconds / 0.65) % 2 == 0) ? "dance1" : "dance2"; }
                else if (behavior.StartsWith("expression:")) pose = behavior.Substring("expression:".Length);
                else bob = Math.Sin(seconds * 1.3);
                SetPose(pose);
                Top = (int)Math.Round(baseY + bob);
                sleepText = behavior == "sleep" ? "Z" : "";
                Invalidate();
            }
            if ((now - lastSave).TotalSeconds >= 15) { SaveState(); lastSave = now; }
            CheckEvolution();
        }

        private void ChooseBehavior()
        {
            double roll = random.NextDouble();
            if (roll < 0.08)
            {
                behavior = "dance";
                behaviorUntil = DateTime.Now.AddSeconds(3.2 + random.NextDouble() * 2.2);
            }
            else if (roll < 0.30)
            {
                behavior = "wander";
                direction = random.Next(2) == 0 ? -1 : 1;
                behaviorUntil = DateTime.Now.AddSeconds(3.5 + random.NextDouble() * 3.5);
                SelectSprite();
            }
            else if (roll < 0.44)
            {
                behavior = "idle";
                behaviorUntil = DateTime.Now.AddSeconds(2.5 + random.NextDouble() * 3);
            }
            else if (roll < 0.51)
            {
                behavior = "stretch";
                behaviorUntil = DateTime.Now.AddSeconds(2.0 + random.NextDouble() * 1.4);
            }
            else if (roll < 0.58)
            {
                behavior = "hop";
                behaviorUntil = DateTime.Now.AddSeconds(1.8 + random.NextDouble());
            }
            else if (roll < 0.65)
            {
                behavior = "celebrate";
                behaviorUntil = DateTime.Now.AddSeconds(2.2 + random.NextDouble() * 1.2);
            }
            else if (roll < 0.73)
            {
                behavior = "run";
                direction = random.Next(2) == 0 ? -1 : 1;
                behaviorUntil = DateTime.Now.AddSeconds(2.4 + random.NextDouble() * 1.6);
            }
            else if (roll < 0.80)
            {
                behavior = "pushup";
                behaviorUntil = DateTime.Now.AddSeconds(4.0 + random.NextDouble() * 2.0);
            }
            else if (roll < 0.87)
            {
                behavior = "paper";
                behaviorUntil = DateTime.Now.AddSeconds(5.0 + random.NextDouble() * 3.0);
            }
            else if (roll < 0.92)
            {
                behavior = "sleep";
                behaviorUntil = DateTime.Now.AddSeconds(4 + random.NextDouble() * 4);
            }
            else
            {
                string branch = Convert.ToString(Get(state, "stage2_path", ""));
                string[] expressions = stage == 2 && branch == "gaming" ? new string[] { "surprised", "contempt", "angry", "happy", "shh" } : new string[] { "surprised", "contempt", "angry", "happy" };
                behavior = "expression:" + expressions[random.Next(expressions.Length)];
                behaviorUntil = DateTime.Now.AddSeconds(1.8 + random.NextDouble() * 1.4);
            }
        }

        private void StartTalking()
        {
            recommendedTrack = null;
            List<string> lines;
            string branch = Convert.ToString(Get(state, "stage2_path", ""));
            if (stage == 2 && !String.IsNullOrEmpty(branch) && branchDialogues.TryGetValue(branch, out lines)) { }
            else if (!dialogues.TryGetValue(stage, out lines) || lines.Count == 0) { ScheduleNextTalk(); return; }
            speech = lines[random.Next(lines.Count)];
            behavior = "talk";
            behaviorUntil = DateTime.Now.AddSeconds(Math.Max(4.2, Math.Min(7.0, 2.8 + speech.Length * 0.13)));
            SetPose("talk");
            ScheduleNextTalk();
        }

        private void ScheduleNextTalk()
        {
            int minimum = Math.Max(5, ToInt(Get(settings, "talk_interval_min_seconds", 18)));
            int maximum = Math.Max(minimum + 1, ToInt(Get(settings, "talk_interval_max_seconds", 32)));
            ScheduleNextTalk(minimum, maximum);
        }

        private void ScheduleNextTalk(int minimum, int maximum)
        {
            nextTalk = DateTime.Now.AddSeconds(minimum + random.NextDouble() * Math.Max(1, maximum - minimum));
        }

        private void SetPose(string pose)
        {
            if (pose == currentPose) return;
            currentPose = pose;
            SelectSprite();
        }

        private void SelectSprite()
        {
            string branch = Convert.ToString(Get(state, "stage2_path", ""));
            if (stage == 2 && Array.IndexOf(StatKeys, branch) < 0) branch = "sports";
            string key = stage == 2 ? BranchSpriteKey(branch, currentPose) : SpriteKey(stage, currentPose);
            currentSprite = direction < 0 ? leftSprites[key] : rightSprites[key];
            Invalidate();
        }

        private void OnPetMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { menu.Show(this, e.Location); return; }
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            movedDuringDrag = false;
            dragStart = Cursor.Position;
            formStart = Location;
        }

        private void OnPetMouseMove(object sender, MouseEventArgs e)
        {
            Cursor = recommendedTrack != null && !dragging && new Rectangle(10, 8, Width - 20, 76).Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (!dragging) return;
            Point current = Cursor.Position;
            int dx = current.X - dragStart.X;
            int dy = current.Y - dragStart.Y;
            if (Math.Abs(dx) + Math.Abs(dy) > 5) movedDuringDrag = true;
            int x = Clamp(formStart.X + dx, workArea.Left, workArea.Right - Width);
            int y = Clamp(formStart.Y + dy, workArea.Top, workArea.Bottom - Height);
            Location = new Point(x, y);
        }

        private void OnPetMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = false;
            if (!movedDuringDrag && recommendedTrack != null && new Rectangle(10, 8, Width - 20, 76).Contains(e.Location))
            {
                OpenRecommendedTrack();
                return;
            }
            if (movedDuringDrag)
            {
                baseY = Top;
                state["x"] = Left;
                SaveState();
                SetPose("idle");
            }
            else Pet();
        }

        private void Pet()
        {
            if ((DateTime.Now - lastPet).TotalSeconds < 0.55) return;
            lastPet = DateTime.Now;
            state["pet_count"] = ToInt(Get(state, "pet_count", 0)) + 1;
            ShowExpression("happy", random.Next(3) == 0 ? "좋아!" : "♥ +1", 1.5);
            SaveState();
            CheckEvolution();
        }

        private void Feed()
        {
            double now = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            int cooldown = ToInt(Get(settings, "feed_cooldown_seconds", 60));
            int remaining = cooldown - (int)(now - ToDouble(Get(state, "last_feed_epoch", 0.0)));
            if (remaining > 0) { ShowNote(remaining + "초 뒤!"); return; }
            state["last_feed_epoch"] = now;
            state["feed_count"] = ToInt(Get(state, "feed_count", 0)) + 1;
            ShowExpression("cheer", "냠냠! +1", 2.0);
            SaveState();
            CheckEvolution();
        }

        private void ShowNote(string text)
        {
            note = text;
            noteTimer.Stop();
            noteTimer.Start();
            Invalidate();
        }

        private void ShowExpression(string expression, string message, double seconds)
        {
            speech = "";
            recommendedTrack = null;
            behavior = "expression:" + expression;
            behaviorUntil = DateTime.Now.AddSeconds(seconds);
            SetPose(expression);
            if (!String.IsNullOrEmpty(message)) ShowNote(message);
        }

        private void ItemTick(object sender, EventArgs e)
        {
            if (activeItem == null && DateTime.Now >= nextItem && !evolving) SpawnItem();
        }

        private void ScheduleNextItem()
        {
            int minimum = Math.Max(8, ToInt(Get(settings, "item_interval_min_seconds", 25)));
            int maximum = Math.Max(minimum + 1, ToInt(Get(settings, "item_interval_max_seconds", 45)));
            ScheduleNextItem(minimum, maximum);
        }

        private void ScheduleNextItem(int minimum, int maximum)
        {
            nextItem = DateTime.Now.AddSeconds(minimum + random.NextDouble() * Math.Max(1, maximum - minimum));
        }

        private void SpawnItem()
        {
            if (activeItem != null) return;
            List<string> kinds = new List<string>();
            string filter = GetDropFilter();
            if (filter == "auto" || filter == "exercise") { kinds.Add("running_shoe"); kinds.Add("pushup_bars"); }
            if (filter == "auto" || filter == "study") { kinds.Add("research_paper"); kinds.Add("linkedin"); }
            if (filter == "auto" || filter == "food") { kinds.Add("chicken"); kinds.Add("pork"); }
            if (filter == "auto" || filter == "sports") { kinds.Add("kt_wiz"); kinds.Add("arsenal"); }
            if (filter == "auto" || filter == "gaming") { kinds.Add("tft"); kinds.Add("minecraft"); }
            kinds.Add("tomato");
            kinds.Add("lettuce");
            string kind = kinds[random.Next(kinds.Count)];
            lastSpawnedItemKind = kind;
            activeItem = new InteractionItemForm(kind, this, ToInt(Get(settings, "item_lifetime_seconds", 20)));
            Rectangle area = workArea;
            int x = random.Next(area.Left + 30, Math.Max(area.Left + 31, area.Right - 110));
            int y = random.Next(area.Top + 80, Math.Max(area.Top + 81, area.Bottom - 180));
            activeItem.Location = new Point(x, y);
            activeItem.FormClosed += delegate { activeItem = null; ScheduleNextItem(); };
            activeItem.Show();
            ShowExpression("surprised", ItemLabel(kind) + " 등장!", 2.0);
        }

        internal Rectangle PetDropBounds()
        {
            return RectangleToScreen(new Rectangle(Width / 2 - 80, Height - spriteHeight - 20, 160, spriteHeight + 20));
        }

        internal void AcceptItem(string kind)
        {
            Dictionary<string, object> stats = EnsureNestedState("stats", EmptyStats());
            Dictionary<string, object> counts = EnsureNestedState("item_counts", EmptyItemCounts());
            counts[kind] = ToInt(Get(counts, kind, 0)) + 1;
            if (kind == "lettuce")
            {
                List<string> candidates = new List<string>();
                foreach (string key in StatKeys) if (ToInt(Get(stats, key, 0)) > 0) candidates.Add(key);
                if (candidates.Count == 0) { ShowExpression("contempt", "상추 함정! 감소할 능력치가 없다.", 2.8); SaveState(); SuggestMusic(); return; }
                string lost = candidates[random.Next(candidates.Count)];
                stats[lost] = Math.Max(0, ToInt(Get(stats, lost, 0)) - 1);
                ShowExpression("angry", "상추 함정! " + StatLabel(lost) + " -1", 2.8);
                SaveState();
                SuggestMusic();
                return;
            }
            string stat = (kind == "running_shoe" || kind == "pushup_bars") ? "exercise"
                : (kind == "research_paper" || kind == "linkedin") ? "study"
                : (kind == "chicken" || kind == "pork" || kind == "tomato") ? "food"
                : (kind == "kt_wiz" || kind == "arsenal") ? "sports" : "gaming";
            stats[stat] = ToInt(Get(stats, stat, 0)) + 1;
            state["last_stat"] = stat;
            string reaction = stat == "sports" ? "cheer" : stat == "study" ? "paper" : "happy";
            ShowExpression(reaction, StatLabel(stat) + " +1", 2.8);
            SaveState();
            CheckEvolution();
            if (evolving) playMusicAfterEvolution = true;
            else SuggestMusic();
        }

        private string CurrentMusicPoolKey()
        {
            if (stage == 1) return "1";
            if (stage == 3) return "3";
            string branch = Convert.ToString(Get(state, "stage2_path", "sports"));
            if (Array.IndexOf(StatKeys, branch) < 0) branch = "sports";
            return "2_" + branch;
        }

        private void SuggestMusic()
        {
            if (!ToBool(Get(state, "music_enabled", true)) || evolving) return;
            MusicTrack[] pool;
            if (!musicPools.TryGetValue(CurrentMusicPoolKey(), out pool) || pool == null || pool.Length == 0) return;
            string previous = Convert.ToString(Get(state, "last_music_video", ""));
            List<MusicTrack> candidates = new List<MusicTrack>();
            foreach (MusicTrack track in pool) if (pool.Length == 1 || track.video_id != previous) candidates.Add(track);
            MusicTrack selected = candidates[random.Next(candidates.Count)];
            state["last_music_video"] = selected.video_id;
            SaveState();
            ShowMusicRecommendation(selected);
        }

        private void ShowMusicRecommendation(MusicTrack selected)
        {
            recommendedTrack = selected;
            speech = "♪ " + selected.title + "\n클릭해서 듣기";
            behavior = "talk";
            behaviorUntil = DateTime.Now.AddSeconds(12);
            SetPose("talk");
            Invalidate();
        }

        private void OpenRecommendedTrack()
        {
            if (recommendedTrack == null || String.IsNullOrEmpty(recommendedTrack.video_id)) return;
            try { Process.Start("https://www.youtube.com/watch?v=" + recommendedTrack.video_id); }
            catch { ShowNote("YouTube를 열 수 없습니다."); }
        }

        internal void RejectItem()
        {
            ShowExpression("contempt", "거긴 아닌데?", 2.0);
        }

        private Dictionary<string, object> EnsureNestedState(string key, Dictionary<string, object> fallback)
        {
            Dictionary<string, object> current = AsDictionary(Get(state, key, null));
            if (current.Count == 0) { current = fallback; state[key] = current; }
            return current;
        }

        private static Dictionary<string, object> EmptyStats()
        {
            return Dict("exercise", 0, "study", 0, "food", 0, "sports", 0, "gaming", 0);
        }

        private static Dictionary<string, object> EmptyUnlockedPaths()
        {
            return Dict("exercise", false, "study", false, "food", false, "sports", false, "gaming", false);
        }

        private static Dictionary<string, object> EmptyItemCounts()
        {
            return Dict("running_shoe", 0, "pushup_bars", 0, "research_paper", 0, "linkedin", 0,
                "chicken", 0, "pork", 0, "tomato", 0, "lettuce", 0,
                "kt_wiz", 0, "arsenal", 0, "tft", 0, "minecraft", 0);
        }

        internal static string StatLabel(string key)
        {
            for (int i = 0; i < StatKeys.Length; i++) if (StatKeys[i] == key) return StatLabels[i];
            return key;
        }

        internal static string ItemLabel(string kind)
        {
            if (kind == "running_shoe") return "아디다스 러닝화";
            if (kind == "pushup_bars") return "팔굽혀펴기 바";
            if (kind == "research_paper") return "논문";
            if (kind == "linkedin") return "링크드인";
            if (kind == "chicken") return "닭고기";
            if (kind == "pork") return "돼지고기";
            if (kind == "tomato") return "토마토";
            if (kind == "lettuce") return "수상한 양상추";
            if (kind == "kt_wiz") return "KT wiz 경기";
            if (kind == "arsenal") return "Arsenal 경기";
            if (kind == "tft") return "롤토체스";
            if (kind == "minecraft") return "마인크래프트";
            return kind;
        }

        private void CheckEvolution()
        {
            if (evolving) return;
            Dictionary<string, object> stats = EnsureNestedState("stats", EmptyStats());
            int stage2Threshold = Math.Max(1, ToInt(Get(settings, "stage2_stat_threshold", 10)));
            int stage3Threshold = Math.Max(1, ToInt(Get(settings, "stage3_each_stat_threshold", 20)));
            Dictionary<string, object> unlocked = EnsureNestedState("unlocked_paths", EmptyUnlockedPaths());
            string newlyUnlocked = "";
            foreach (string key in StatKeys)
                if (ToInt(Get(stats, key, 0)) >= stage2Threshold && !ToBool(Get(unlocked, key, false)))
                {
                    unlocked[key] = true;
                    newlyUnlocked = key;
                }
            if (!String.IsNullOrEmpty(newlyUnlocked))
            {
                SaveState();
                if (stage != 1 || ToBool(Get(state, "stage2_evolution_seen", false))) ShowNote(Stage2Name(newlyUnlocked) + " 해금!");
            }
            bool finalReady = true;
            foreach (string key in StatKeys) if (ToInt(Get(stats, key, 0)) < stage3Threshold) finalReady = false;
            if (finalReady && !IsFinalUnlocked())
            {
                StartEvolution(3, "");
                return;
            }
            if (!ToBool(Get(state, "stage2_evolution_seen", false)) && stage == 1)
            {
                string path = HighestStat(stats);
                if (ToInt(Get(stats, path, 0)) >= stage2Threshold) StartEvolution(2, path);
            }
        }

        private string HighestStat(Dictionary<string, object> stats)
        {
            int maximum = Int32.MinValue;
            List<string> tied = new List<string>();
            foreach (string key in StatKeys)
            {
                int value = ToInt(Get(stats, key, 0));
                if (value > maximum) { maximum = value; tied.Clear(); tied.Add(key); }
                else if (value == maximum) tied.Add(key);
            }
            string recent = Convert.ToString(Get(state, "last_stat", ""));
            if (tied.Contains(recent)) return recent;
            return tied.Count > 0 ? tied[0] : StatKeys[0];
        }

        private void StartEvolution(int target, string path)
        {
            if (evolving || target <= stage || target > 3) return;
            currentPose = "idle";
            SelectSprite();
            evolving = true;
            evolutionTarget = target;
            pendingEvolutionPath = path;
            evolutionFrame = 0;
            evolutionTimer.Interval = 190;
            evolutionSilhouette = true;
            if (silhouetteSprite != null) { silhouetteSprite.Dispose(); silhouetteSprite = null; }
            if (nextSilhouetteSprite != null) { nextSilhouetteSprite.Dispose(); nextSilhouetteSprite = null; }
            silhouetteSprite = MakeBlackSilhouette(currentSprite);
            nextSilhouetteSprite = MakeBlackSilhouette(GetEvolutionTargetSprite(target, path));
            sleepText = "";
            speech = "";
            recommendedTrack = null;
            noteTimer.Stop();
            note = "";
            Invalidate();
            evolutionTimer.Start();
        }

        private void EvolutionTick(object sender, EventArgs e)
        {
            if (evolutionFrame == 6) evolutionTimer.Interval = 130;
            if (evolutionFrame == 14) evolutionTimer.Interval = 80;
            if (evolutionFrame == 22) evolutionTimer.Interval = 55;
            if (evolutionFrame >= 28)
            {
                evolutionTimer.Stop();
                stage = evolutionTarget;
                state["stage"] = stage;
                if (stage == 2 && !String.IsNullOrEmpty(pendingEvolutionPath))
                {
                    state["stage2_path"] = pendingEvolutionPath;
                    state["stage2_evolution_seen"] = true;
                }
                if (stage == 3) state["final_unlocked"] = true;
                currentPose = "idle";
                SelectSprite();
                evolving = false;
                evolutionSilhouette = false;
                if (silhouetteSprite != null) { silhouetteSprite.Dispose(); silhouetteSprite = null; }
                if (nextSilhouetteSprite != null) { nextSilhouetteSprite.Dispose(); nextSilhouetteSprite = null; }
                SaveState();
                ShowNote(CurrentStageName() + "!");
                if (playMusicAfterEvolution)
                {
                    playMusicAfterEvolution = false;
                    SuggestMusic();
                }
                return;
            }
            evolutionFrame++;
            Invalidate();
        }

        private Bitmap GetEvolutionTargetSprite(int target, string path)
        {
            string key;
            if (target == 2)
            {
                if (Array.IndexOf(StatKeys, path) < 0) path = "sports";
                key = BranchSpriteKey(path, "idle");
            }
            else key = SpriteKey(target, "idle");
            Dictionary<string, Bitmap> sprites = direction < 0 ? leftSprites : rightSprites;
            Bitmap targetSprite;
            return sprites.TryGetValue(key, out targetSprite) ? targetSprite : currentSprite;
        }

        private static Bitmap MakeBlackSilhouette(Bitmap source)
        {
            if (source == null) return null;
            Bitmap result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                {
                    int alpha = source.GetPixel(x, y).A;
                    if (alpha > 0) result.SetPixel(x, y, Color.FromArgb(alpha, 0, 0, 0));
                }
            return result;
        }

        private void ShowStatus()
        {
            Dictionary<string, object> stats = EnsureNestedState("stats", EmptyStats());
            using (StatChartForm chart = new StatChartForm(
                CurrentStageName(), stage, Convert.ToString(Get(state, "stage2_path", "")), GetDropFilter(), stats,
                ToInt(Get(settings, "stage2_stat_threshold", 10)), ToInt(Get(settings, "stage3_each_stat_threshold", 20))))
                chart.ShowDialog(this);
        }

        private void ResetProgress()
        {
            if (MessageBox.Show(this, "진화와 돌보기 기록을 모두 초기화할까요?", "진화 초기화", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int x = Left;
            state = DefaultState();
            state["x"] = x;
            stage = 1;
            SelectSprite();
            SaveState();
            ShowNote("다시 시작!");
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                    return key != null && key.GetValue(AppName) != null;
            }
            catch { return false; }
        }

        private void ToggleStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enabled) key.SetValue(AppName, "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue(AppName, false);
                }
            }
            catch (Exception error) { MessageBox.Show(this, error.Message, "시작 프로그램 설정", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        private void SaveState()
        {
            state["stage"] = stage;
            state["x"] = Left;
            try
            {
                string temporary = statePath + ".tmp";
                File.WriteAllText(temporary, json.Serialize(state), Encoding.UTF8);
                if (File.Exists(statePath)) File.Delete(statePath);
                File.Move(temporary, statePath);
            }
            catch { }
        }

        private void OnPetClosing(object sender, FormClosingEventArgs e)
        {
            SaveState();
            foreach (Bitmap image in rightSprites.Values) image.Dispose();
            foreach (Bitmap image in leftSprites.Values) image.Dispose();
            if (silhouetteSprite != null) silhouetteSprite.Dispose();
            if (nextSilhouetteSprite != null) nextSilhouetteSprite.Dispose();
            if (activeItem != null && !activeItem.IsDisposed) activeItem.Close();
        }

        internal static object Get(Dictionary<string, object> dictionary, string key, object fallback)
        {
            object value;
            return dictionary != null && dictionary.TryGetValue(key, out value) ? value : fallback;
        }

        private static Dictionary<string, object> AsDictionary(object value)
        {
            Dictionary<string, object> dictionary = value as Dictionary<string, object>;
            if (dictionary != null) return dictionary;
            return new Dictionary<string, object>();
        }

        internal static int ToInt(object value) { try { return Convert.ToInt32(value); } catch { return 0; } }
        private static double ToDouble(object value) { try { return Convert.ToDouble(value); } catch { return 0; } }
        private static bool ToBool(object value) { try { return Convert.ToBoolean(value); } catch { return false; } }
        private static int Clamp(int value, int minimum, int maximum) { return Math.Max(minimum, Math.Min(maximum, value)); }
        private string CurrentStageName()
        {
            if (stage == 1) return "관호";
            if (stage == 3) return "관종대왕";
            string path = Convert.ToString(Get(state, "stage2_path", ""));
            return String.IsNullOrEmpty(path) ? "관계인" : Stage2Name(path);
        }

        internal static string Stage2Name(string path)
        {
            if (path == "exercise") return "수원고 달리기 1등출신";
            if (path == "study") return "master in England";
            if (path == "food") return "관주부";
            if (path == "sports") return "관계인";
            if (path == "gaming") return "관이커";
            return "관계인";
        }

        private static string FormatDuration(double seconds)
        {
            int totalMinutes = (int)(seconds / 60);
            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return hours > 0 ? hours + "시간 " + minutes + "분" : minutes + "분";
        }
    }

    internal sealed class StatChartForm : Form
    {
        private readonly string stageName;
        private readonly int stage;
        private readonly string path;
        private readonly string preferredPath;
        private readonly Dictionary<string, object> stats;
        private readonly int stage2Threshold;
        private readonly int stage3Threshold;

        public StatChartForm(string stageName, int stage, string path, string preferredPath, Dictionary<string, object> stats, int stage2Threshold, int stage3Threshold)
        {
            this.stageName = stageName;
            this.stage = stage;
            this.path = path;
            this.preferredPath = preferredPath;
            this.stats = stats;
            this.stage2Threshold = Math.Max(1, stage2Threshold);
            this.stage3Threshold = Math.Max(1, stage3Threshold);
            Text = "진화 능력치";
            ClientSize = new Size(560, 610);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(12, 31, 49);
            DoubleBuffered = true;
            Paint += OnPaintChart;
        }

        private void OnPaintChart(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color sky = Color.FromArgb(135, 206, 235);
            Color grid = Color.FromArgb(80, 135, 164);
            Color fillColor = Color.FromArgb(105, 79, 185, 242);
            using (Font title = new Font("Malgun Gothic", 20, FontStyle.Bold))
            using (Font subtitle = new Font("Malgun Gothic", 10.5f, FontStyle.Regular))
            using (Brush skyBrush = new SolidBrush(sky))
            {
                g.DrawString(stage + "단계 · " + stageName, title, skyBrush, new PointF(24, 18));
                string guide = "각 능력치 " + stage2Threshold + ": 해당 2단계 해금 · 모든 능력치 " + stage3Threshold + ": 관종대왕 해금";
                g.DrawString(guide, subtitle, skyBrush, new PointF(26, 59));
                string filterLabel = preferredPath == "auto" ? "모든 종류" : PetForm.StatLabel(preferredPath);
                g.DrawString("현재 오브젝트 드롭: " + filterLabel, subtitle, skyBrush, new PointF(26, 83));
            }

            PointF center = new PointF(280, 322);
            float radius = 164;
            int scaleMaximum = Math.Max(stage3Threshold, 1);
            PointF[] outer = Pentagon(center, radius);
            using (Pen gridPen = new Pen(grid, 1.5f))
            {
                for (int ring = 1; ring <= 4; ring++) g.DrawPolygon(gridPen, Pentagon(center, radius * ring / 4f));
                foreach (PointF point in outer) g.DrawLine(gridPen, center, point);
            }

            PointF[] values = new PointF[5];
            for (int i = 0; i < 5; i++)
            {
                float ratio = Math.Min(1f, PetForm.ToInt(PetForm.Get(stats, PetForm.StatKeys[i], 0)) / (float)scaleMaximum);
                values[i] = PointOnPentagon(center, radius * ratio, i);
            }
            using (Brush fill = new SolidBrush(fillColor)) g.FillPolygon(fill, values);
            using (Pen valuePen = new Pen(sky, 3)) g.DrawPolygon(valuePen, values);
            foreach (PointF point in values) using (Brush dot = new SolidBrush(sky)) g.FillEllipse(dot, point.X - 5, point.Y - 5, 10, 10);

            using (Font labelFont = new Font("Malgun Gothic", 11, FontStyle.Bold))
            using (Brush labelBrush = new SolidBrush(Color.White))
            using (StringFormat centerText = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                for (int i = 0; i < 5; i++)
                {
                    PointF labelPoint = PointOnPentagon(center, radius + 46, i);
                    int value = PetForm.ToInt(PetForm.Get(stats, PetForm.StatKeys[i], 0));
                    g.DrawString(PetForm.StatLabels[i] + "\n" + value, labelFont, labelBrush, new RectangleF(labelPoint.X - 65, labelPoint.Y - 25, 130, 50), centerText);
                }
            }

            using (Font footer = new Font("Malgun Gothic", 9.5f))
            using (Brush muted = new SolidBrush(Color.FromArgb(190, 218, 232)))
                g.DrawString("차트 최대 눈금: 3단계 기준 " + stage3Threshold, footer, muted, new PointF(20, 564));
        }

        private static PointF[] Pentagon(PointF center, float radius)
        {
            PointF[] points = new PointF[5];
            for (int i = 0; i < 5; i++) points[i] = PointOnPentagon(center, radius, i);
            return points;
        }

        private static PointF PointOnPentagon(PointF center, float radius, int index)
        {
            double angle = -Math.PI / 2 + index * Math.PI * 2 / 5;
            return new PointF(center.X + (float)Math.Cos(angle) * radius, center.Y + (float)Math.Sin(angle) * radius);
        }
    }

    internal sealed class InteractionItemForm : Form
    {
        private readonly string kind;
        private readonly PetForm ownerPet;
        private readonly Timer lifetimeTimer = new Timer();
        private readonly Bitmap itemImage;
        private Point dragStart;
        private Point formStart;
        private bool dragging;

        public InteractionItemForm(string kind, PetForm ownerPet, int lifetimeSeconds)
        {
            this.kind = kind;
            this.ownerPet = ownerPet;
            string itemPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "items", kind + ".png");
            itemImage = new Bitmap(itemPath);
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Width = 82;
            Height = 82;
            BackColor = Color.FromArgb(1, 2, 3);
            TransparencyKey = BackColor;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            MouseDown += OnDragStart;
            MouseMove += OnDragging;
            MouseUp += OnDragEnd;
            Paint += OnPaintItem;
            lifetimeTimer.Interval = Math.Max(5, lifetimeSeconds) * 1000;
            lifetimeTimer.Tick += delegate { lifetimeTimer.Stop(); Close(); };
            lifetimeTimer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return parameters;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            ShowInactiveTopmost(this.Handle);
            string qaFocusPath = Environment.GetEnvironmentVariable("EVOPET_QA_FOCUS_PATH");
            if (!String.IsNullOrEmpty(qaFocusPath))
            {
                try { File.WriteAllText(qaFocusPath, "0x" + GetWindowLong(this.Handle, -20).ToString("X8")); }
                catch { }
            }
            base.OnShown(e);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);

        private static void ShowInactiveTopmost(IntPtr handle)
        {
            const uint SWP_NOSIZE = 0x0001;
            const uint SWP_NOMOVE = 0x0002;
            const uint SWP_NOACTIVATE = 0x0010;
            const uint SWP_SHOWWINDOW = 0x0040;
            SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }

        private void OnDragStart(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragStart = Cursor.Position;
            formStart = Location;
            lifetimeTimer.Stop();
        }

        private void OnDragging(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point cursor = Cursor.Position;
            Location = new Point(formStart.X + cursor.X - dragStart.X, formStart.Y + cursor.Y - dragStart.Y);
        }

        private void OnDragEnd(object sender, MouseEventArgs e)
        {
            if (!dragging || e.Button != MouseButtons.Left) return;
            dragging = false;
            if (ownerPet.PetDropBounds().IntersectsWith(Bounds))
            {
                ownerPet.AcceptItem(kind);
                Close();
            }
            else
            {
                ownerPet.RejectItem();
                lifetimeTimer.Interval = 8000;
                lifetimeTimer.Start();
            }
        }

        private void OnPaintItem(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle orb = new Rectangle(5, 5, 72, 72);
            using (Brush glow = new SolidBrush(Color.FromArgb(225, 12, 31, 49))) g.FillEllipse(glow, orb);
            using (Pen border = new Pen(kind == "lettuce" ? Color.FromArgb(245, 83, 72) : Color.FromArgb(135, 206, 235), 2.5f)) g.DrawEllipse(border, orb);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(itemImage, new Rectangle(10, 10, 62, 62));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            lifetimeTimer.Stop();
            lifetimeTimer.Dispose();
            itemImage.Dispose();
            base.OnFormClosed(e);
        }
    }

    internal sealed class MusicTrack
    {
        public string title { get; set; }
        public string video_id { get; set; }
        public string source_playlist { get; set; }
    }

    internal static class GraphicsExtensions
    {
        public static void FillRoundedRectangle(this Graphics graphics, Brush brush, Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
                path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
                path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                graphics.FillPath(brush, path);
            }
        }
    }
}
