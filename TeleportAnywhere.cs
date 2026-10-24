using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using m2d;
using nel;
using evt;
using XX;

[BepInPlugin("org.wcxawa.teleport-anywhere", "Teleport Anywhere", "0.2.2")]
[BepInDependency("org.aliceincradle.kaleidoscopic", BepInDependency.DependencyFlags.SoftDependency)]
public sealed partial class TeleportAnywhere : BaseUnityPlugin
{
    public static TeleportAnywhere Instance { get; private set; }
    public sealed class Destination
    {
        public string MapKey, Name, Area, Kind;
        public int X, Y;
        public Destination Copy() { return (Destination)MemberwiseClone(); }
        public string Id { get { return MapKey + ":" + X + ":" + Y; } }
    }
    readonly List<Destination> destinations = new List<Destination>();
    readonly List<Destination> bookmarks = new List<Destination>();
    ConfigEntry<Key> hotkey;
    ConfigEntry<bool> launcher;
    ConfigEntry<bool> extraPanel;
    Harmony harmony;
    NelM2DBase sessionWorld;
    Destination selected, pending, origin, returnPoint;
    bool show, travelling, sawEvent;
    float travelStarted, noticeUntil;
    string notice = "", search = "", bookmarkName = "";
    Vector2 scroll;
    static readonly Regex MapKeyPattern = new Regex("^[A-Za-z0-9_]+$");

    void Awake()
    {
        Instance = this;
        hotkey = Config.Bind("Controls", "Hotkey", Key.F9, "打开原版地图；启用额外面板时改为打开额外面板。");
        launcher = Config.Bind("Controls", "ShowButton", true, "启用额外面板后，显示其左上角传送入口。");
        extraPanel = Config.Bind("Controls", "EnableExtraPanel", false, "启用旧版独立面板及临时标记。默认关闭，直接使用原版长椅地图传送。");
        harmony = new Harmony("org.wcxawa.teleport-anywhere");
        InstallInput();
        InstallNativeMap();
        harmony.Patch(AccessTools.Method(typeof(SceneGame), "runIRD"), prefix: new HarmonyMethod(typeof(TeleportAnywhere), "PauseWorld"));
        harmony.Patch(AccessTools.Method(typeof(SceneGame), "FixedUpdate"), prefix: new HarmonyMethod(typeof(TeleportAnywhere), "PausePhysics"));
        harmony.Patch(AccessTools.Method(typeof(COOK), "initGameScene"), prefix: new HarmonyMethod(typeof(TeleportAnywhere), "BeforeLoadSession"));
        harmony.Patch(AccessTools.Method(typeof(COOK), "setLoadTarget"), prefix: new HarmonyMethod(typeof(TeleportAnywhere), "BeforeLoadSession"));
        Logger.LogInfo("Teleport Anywhere 0.2.2 loaded. Independent native map travel enabled.");
    }
    void OnDestroy()
    {
        show = false;
        if(harmony != null)harmony.UnpatchSelf();
        ReleaseTheme();
        if(Instance == this)Instance = null;
    }
    void OnDisable() { show = false; releaseInput = true; }
    static bool PauseWorld(ref bool __result)
    {
        if(Instance == null || !Instance.show)return true;
        __result = true;
        return false;
    }
    static bool PausePhysics() { return Instance == null || !Instance.show; }
    static void BeforeLoadSession() { if(Instance != null)Instance.ResetSession(); }
    static NelM2DBase World { get { return M2DBase.Instance as NelM2DBase; } }

    void Update()
    {
        PollInput();
        NelM2DBase world = World;
        if(world == null)
        {
            if(!travelling && sessionWorld != null)ResetSession();
            return;
        }
        // Ordinary native transfers also temporarily have no current map.
        if(travelling && (EV.isActive() || world.transferring_game_stopping))sawEvent = true;
        if(world.curMap == null) { if(show)SetVisible(false); return; }
        if(sessionWorld != world)
        {
            ResetSession(); sessionWorld = world;
        }
        if(!travelling)return;
        if(EV.isActive() || world.transferring_game_stopping)sawEvent = true;
        if(!EV.isActive() && !world.transferring_game_stopping && Time.realtimeSinceStartup - travelStarted > .5f)
        {
            bool arrived = pending != null && world.curMap.key == pending.MapKey &&
                Mathf.Abs(world.PlayerNoel.x - (pending.X + .5f)) < 7 && Mathf.Abs(world.PlayerNoel.mbottom - pending.Y) < 14;
            if(arrived && sawEvent)
            {
                returnPoint = origin;
                Tell("已抵达 " + pending.Name + (extraPanel.Value ? "。可以使用「返回出发点」。" : "。"), 8);
                Logger.LogInfo("Arrived: " + pending.Id + " actual=" + world.PlayerNoel.x + "," + world.PlayerNoel.mbottom);
            }
            else Tell("传送被原版事件取消，或落点被剧情调整。请检查当前地图。", 10);
            travelling = false; pending = origin = null;
        }
        else if(Time.realtimeSinceStartup - travelStarted > 90 && !sawEvent)
        {
            travelling = false; pending = origin = null;
            Tell("传送未能启动，请查看 BepInEx 日志。", 10);
        }
    }
    void ResetSession()
    {
        SetVisible(false); bookmarks.Clear(); destinations.Clear(); selected = returnPoint = null;
        travelling = sawEvent = false; pending = origin = null;
        sessionWorld = null;
    }
    void Tell(string text, float duration)
    {
        notice = text; noticeUntil = Time.realtimeSinceStartup + duration;
    }
    string BlockReason()
    { return TravelBlockReason(false); }
    string TravelBlockReason(bool nativeMap)
    {
        NelM2DBase w = World;
        if(w == null || w.curMap == null || w.PlayerNoel == null || !w.curMap.isGameMode())return "请先进入游戏并读取存档。";
        if(travelling || w.transferring_game_stopping || w.isMaterialLoading())return "正在切换地图，请稍候。";
        if(EV.isActive() || EV.isLoading())return "对话或事件进行中，请结束后再传送。";
        if(!nativeMap && w.GM != null && w.GM.isActive())return "请先关闭游戏菜单或长椅菜单。";
        PR p = w.PlayerNoel;
        if(!p.is_alive || p.isAbsorbState() || p.isDamagingOrKo() || p.isTrappedState())return "当前角色状态不能传送。";
        if(EnemySummoner.isActiveBorder())return "战斗进行中，请结束战斗后再传送。";
        if(p.isPuzzleManagingMp())return "解谜状态下不能传送。";
        if(w.FlgFastTravelDeclined.isActive() || w.FlgWarpEventNotInjectable.isActive())return "当前场景暂不允许传送。";
        // The native menu itself disables opening another menu while active.
        if((!nativeMap && !w.can_open_gamemenu) || (!p.isNormalState() && !p.isOnBench(false)))return "请回到可自由行动的状态。";
        return null;
    }
    void SetVisible(bool value)
    {
        if(show == value)return;
        if(value)
        {
            string reason = BlockReason();
            if(reason != null) { Tell(reason, 5); return; }
            RefreshDestinations();
        }
        show = value; releaseInput = true; neutralInputFrame = -1;
        ClearNativeInput(null);
    }
    static string MapName(Map2d map)
    {
        string tx = M2MapTitle.get_tx_key(map);
        return tx == null ? map.key : Regex.Replace(TX.Get(tx, map.key), "<[^>]*>", "");
    }
    Destination Capture(string name, string kind)
    {
        NelM2DBase w = World;
        return new Destination { MapKey = w.curMap.key, Name = string.IsNullOrEmpty(name) ? MapName(w.curMap) : name,
            Area = w.WM.CurWM.localized_name, X = Mathf.FloorToInt(w.PlayerNoel.x), Y = Mathf.RoundToInt(w.PlayerNoel.mbottom), Kind = kind };
    }
    void RefreshDestinations()
    {
        string previous = selected == null ? null : selected.Id;
        destinations.Clear();
        NelM2DBase w = World;
        foreach(KeyValuePair<string, WholeMapItem> pair in w.WM.getWholeMapDescriptionObject())
        {
            WholeMapItem area = pair.Value;
            foreach(WMIconPosition icon in KnownBenches(area))
            {
                Map2d map = icon.getDepertureMap();
                if(map == null || !MapKeyPattern.IsMatch(map.key) || map.is_whole)continue;
                Vector2 pos = icon.getDepertureMapPos();
                Destination destination = new Destination { MapKey = map.key, X = (int)pos.x, Y = (int)pos.y,
                    Name = MapName(map), Area = area.localized_name, Kind = "已发现长椅" };
                if(destinations.Find(delegate(Destination other) { return other.Id == destination.Id; }) == null)destinations.Add(destination);
            }
        }
        destinations.Sort(delegate(Destination a, Destination b) { int c = string.Compare(a.Area, b.Area, StringComparison.CurrentCulture); return c == 0 ? string.Compare(a.Name, b.Name, StringComparison.CurrentCulture) : c; });
        destinations.InsertRange(0, bookmarks);
        if(returnPoint != null)destinations.Insert(0, returnPoint);
        selected = destinations.Find(delegate(Destination d) { return d.Id == previous; });
        if(selected == null && destinations.Count > 0)selected = destinations[0];
    }
    void AddBookmark()
    {
        string reason = BlockReason();
        if(reason != null) { Tell(reason, 6); return; }
        if(bookmarks.Count >= 30) { Tell("本次游玩最多记录 30 个传送点，请先删除旧记录。", 6); return; }
        Destination d = Capture(bookmarkName.Trim(), "本次游玩记录");
        bookmarks.RemoveAll(delegate(Destination b) { return b.Id == d.Id; });
        bookmarks.Add(d); bookmarkName = ""; selected = d; RefreshDestinations();
        Tell("已记录当前位置；退出到标题或重新读档后清空。", 6);
    }
    bool BeginTravel(Destination target)
    { return BeginTravelCore(target, false); }
    bool BeginTravelCore(Destination target, bool nativeMap)
    {
        string reason = TravelBlockReason(nativeMap);
        if(reason != null) { Tell(reason, 6); return false; }
        if(target == null)return false;
        NelM2DBase w = World;
        Map2d dest = MapKeyPattern.IsMatch(target.MapKey) ? w.Get(target.MapKey, true) : null;
        if(dest == null || dest.is_whole) { Tell("目的地不可用，请刷新列表。", 6); return false; }
        WholeMapItem srcArea = w.WM.CurWM, dstArea = w.WM.GetWholeFor(dest, true);
        if(srcArea == null || dstArea == null) { Tell("目的地不属于可用的世界区域。", 6); return false; }
        if(target.X < 0 || target.Y < 0 || target.X >= dest.clms || target.Y >= dest.rows) { Tell("传送坐标已失效，请删除此记录。", 6); return false; }
        if(dest == w.curMap && Mathf.Abs(w.PlayerNoel.x - target.X - .5f) < 1.2f && Mathf.Abs(w.PlayerNoel.mbottom - target.Y) < 1.2f)
        { Tell("你已经在这个传送点附近。", 5); return false; }
        try
        {
            string script = BuildTravelScript(target, w, srcArea, dstArea, dest, nativeMap);
            EvReader reader = new EvReader("%TELEPORT_ANYWHERE"); reader.parseText(script);
            origin = Capture("返回 · " + MapName(w.curMap), "上次出发点"); pending = target.Copy();
            travelling = true; sawEvent = false; travelStarted = Time.realtimeSinceStartup;
            SetVisible(false);
            EV.stackReader(reader);
            Logger.LogInfo("Travel requested: " + origin.Id + " -> " + target.Id);
            return true;
        }
        catch(Exception ex)
        {
            travelling = false; pending = origin = null;
            Logger.LogError(ex); Tell("传送启动失败，详见 BepInEx 日志。", 8); return false;
        }
    }
    static string BuildTravelScript(Destination d, NelM2DBase w, WholeMapItem src, WholeMapItem dst, Map2d map, bool nativeMap)
    {
        // Keep native story gates, corruption notifications, async material loading,
        // entry events and area cleanup. The native map also retains bench cure
        // and autosave; the optional position-bookmark panel omits those steps.
        StringBuilder s = new StringBuilder(nativeMap ? "UIGM DEACTIVATE\nDENY_SKIP\nSTOP_LETTERBOX\n" : "DENY_SKIP\nSTOP_LETTERBOX\n");
        if(src != dst)
        {
            using(STB b = TX.PopBld())
            {
                M2LpMapTransferWarp.GoOutOtherAreaScript(b, src, dst, map, null);
                s.Append(b.ToString());
            }
        }
        s.Append("_result=1\nCHANGE_EVENT2 __M2D_FASTTRAVEL_PRE ").Append(d.MapKey).Append(' ').Append(src.text_key)
            .Append("\nIF $_result'==0' SEEK_END\nSEND_EVENT_CORRUPTION PRE_UNLOAD\n");
        s.Append("#MS_ % 'F P[stand] '\nPIC_FILL &9 0x").Append(NEL.FillingBgCol.rgba.ToString("X8"))
            .Append("\nPIC_FADEIN &9 18\nWAIT 20\n");
        s.Append("COOK_ADD_WALK_COUNT ").Append(d.MapKey).Append("\nINIT_MAP_MATERIAL ").Append(d.MapKey)
            .Append(src == dst ? " 1\n" : " 2\n").Append("ENABLE_PUTTED_MAGIC_SAVE\nWAIT_FN MAP_TRANSFER\n");
        if(src != dst && src.safe_area && !dst.safe_area)
            s.Append("@___city_guild/__FLUSH_SAFE_AREA ").Append(src.text_key).Append(' ').Append(dst.text_key).Append(" 0\n");
        s.Append("NEL_EXECUTE_FAST_TRAVEL ").Append(d.MapKey).Append(' ').Append(d.X).Append(' ').Append(d.Y)
            .Append(" 1\nWAIT_MOVE\nPIC_FADEOUT &9 22\n");
        if(src != dst)
        {
            s.Append("CHANGE_EVENT2 __M2D_FLUSHED_AREA ").Append(src.text_key).Append(' ').Append(dst.text_key).Append(' ')
                .Append(w.curMap.key).Append(' ').Append(d.MapKey).Append("\nWAIT_FN REELMNG\n");
            if(src.safe_area && !dst.safe_area)
                s.Append("IFDEF _initialize_night_level {\nDANGER $_initialize_night_level 1\n} ELSE {\nDANGER 0 1\n}\nDANGER_INITIALIZE_MEMORY\n");
        }
        if(nativeMap)s.Append("PR_CURE 0 0 1\nAUTO_SAVE_BENCH\n");
        s.Append("CHANGE_EVENT2 __M2D_FASTTRAVEL_AFTER\nALLOW_SKIP\n");
        return s.ToString();
    }
}
