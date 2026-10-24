using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using nel;
using nel.gm;

public sealed partial class TeleportAnywhere
{
    static readonly Type NativeMapPage = AccessTools.TypeByName("nel.gm.UiGMCMap");
    static readonly FieldInfo MapPages = AccessTools.Field(typeof(UiGameMenu), "AGmcCache");
    static readonly FieldInfo MapSkin = AccessTools.Field(NativeMapPage, "WmSkin");
    static readonly FieldInfo MapController = AccessTools.Field(NativeMapPage, "WmCtr");
    static readonly FieldInfo BenchList = AccessTools.Field(typeof(ButtonSkinWholeMapArea), "ABenchList");
    static readonly FieldInfo CanFastTravel = AccessTools.Field(typeof(UiGameMenu), "can_use_fasttravel");
    static readonly FieldInfo PlayerOnBench = AccessTools.Field(typeof(UiGameMenu), "pr_on_bench");
    static readonly PropertyInfo EffectiveMapTravel = AccessTools.Property(NativeMapPage, "can_use_fasttravel");

    void InstallNativeMap()
    {
        harmony.Patch(AccessTools.PropertyGetter(NativeMapPage, "can_use_fasttravel"),
            postfix: new HarmonyMethod(typeof(TeleportAnywhere), "AfterMapPermission"));
        harmony.Patch(AccessTools.Method(NativeMapPage, "executeFastTravelConfirm"),
            transpiler: new HarmonyMethod(typeof(TeleportAnywhere), "AllowIndependentBenchConfirm"));
        harmony.Patch(AccessTools.Method(NativeMapPage, "fineKD"),
            transpiler: new HarmonyMethod(typeof(TeleportAnywhere), "AllowIndependentMapHelp"));
        harmony.Patch(AccessTools.Method(NativeMapPage, "fnClickFastTravelCmd"),
            prefix: new HarmonyMethod(typeof(TeleportAnywhere), "BeforeMapTravelCommand"),
            postfix: new HarmonyMethod(typeof(TeleportAnywhere), "AfterMapTravelCommand"),
            transpiler: new HarmonyMethod(typeof(TeleportAnywhere), "AllowIndependentBenchConfirm"));
        harmony.Patch(AccessTools.Method(typeof(UiGameMenu), "recheckCanFastTravel"),
            postfix: new HarmonyMethod(typeof(TeleportAnywhere), "AfterNativeEligibility"));
        harmony.Patch(AccessTools.Method(NativeMapPage, "initAppearMain"),
            postfix: new HarmonyMethod(typeof(TeleportAnywhere), "AfterNativeMapAppear"));
        harmony.Patch(AccessTools.Method(NativeMapPage, "initEdit"),
            postfix: new HarmonyMethod(typeof(TeleportAnywhere), "AfterNativeMapAppear"));
        harmony.Patch(AccessTools.Method(typeof(ButtonSkinWholeMapArea), "setWholeMapTarget", new Type[]{typeof(WholeMapItem),typeof(float),typeof(float)}),
            postfix: new HarmonyMethod(typeof(TeleportAnywhere), "AfterNativeMapTarget"));
        harmony.Patch(AccessTools.PropertySetter(typeof(ButtonSkinWholeMapArea), "fast_travel_active"),
            postfix: new HarmonyMethod(typeof(TeleportAnywhere), "AfterNativeTravelMode"));
        harmony.Patch(AccessTools.Method(typeof(UiBenchMenu), "ExecuteFastTravel"),
            prefix: new HarmonyMethod(typeof(TeleportAnywhere), "BeforeNativeTravel"));
    }
    static void AfterMapPermission(object __instance, ref bool __result)
    {
        if(Instance == null || !Instance.isActiveAndEnabled || World == null || World.GM == null)return;
        var pages = MapPages.GetValue(World.GM) as IList;
        if(pages == null || pages.Count <= 5 || pages[5] != __instance)return;
        // Source benches are no longer required by this plugin. Keep the real
        // scenario/weather restrictions; never fabricate GM.BenchChip or flags.
        __result = SCN.isBenchCmdEnable("fast_travel") && !evt.EV.isStoppingGameHandle();
    }
    static IEnumerable<CodeInstruction> AllowIndependentBenchConfirm(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var codes = new List<CodeInstruction>(instructions);
        var bench = AccessTools.PropertyGetter(NativeMapPage, "BenchChip");
        var icon = AccessTools.Method(typeof(NelChipBench), "IconIs", new Type[]{typeof(WMIcon)});
        for(int i = 0; i < codes.Count - 1; i++)
        {
            if(!codes[i].Calls(bench) || !IsFalseBranch(codes[i+1]))continue;
            int next = -1, check = -1;
            for(int j = i+2; j < Math.Min(codes.Count,i+8); j++)if(codes[j].Calls(bench)){next=j;break;}
            if(next < 0)continue;
            for(int j = next+1; j < Math.Min(codes.Count,next+10); j++)if(codes[j].Calls(icon)){check=j;break;}
            if(check < 0 || check+1 >= codes.Count || !IsFalseBranch(codes[check+1]))continue;
            object reject = codes[i+1].operand, allow = codes[check+1].operand;
            // Kaleidoscopic may have already redirected this null branch.
            if(object.Equals(reject,allow))return codes;
            var existingBench = generator.DefineLabel();
            codes[i+2].labels.Add(existingBench);
            var branch = new CodeInstruction(codes[i+1]);
            branch.opcode = OpCodes.Brtrue; branch.operand = existingBench;
            codes[i+1] = branch;
            codes.InsertRange(i+2,new CodeInstruction[]{
                new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(TeleportAnywhere),"NativeMapTravelReady")),
                new CodeInstruction(OpCodes.Brtrue,allow),
                new CodeInstruction(OpCodes.Br,reject)
            });
            return codes;
        }
        throw new InvalidOperationException("Unsupported native map bench-confirmation IL; cannot enable independent map travel.");
    }
    static bool IsFalseBranch(CodeInstruction code)
    { return code.opcode == OpCodes.Brfalse || code.opcode == OpCodes.Brfalse_S; }
    static IEnumerable<CodeInstruction> AllowIndependentMapHelp(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var bench = AccessTools.PropertyGetter(NativeMapPage,"BenchChip");
        var icon = AccessTools.Method(typeof(NelChipBench),"IconIs",new Type[]{typeof(WMIcon)});
        bool context = false, safeIcon = false;
        for(int i = 0; i < codes.Count; i++)
        {
            if(codes[i].Calls(bench) && i+1 < codes.Count && IsFalseBranch(codes[i+1]))
            {
                codes.Insert(++i,new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(TeleportAnywhere),"HasMapTravelContext")));
                context = true;
            }
            else if(codes[i].Calls(icon))
            {
                var call = new CodeInstruction(codes[i]);
                call.opcode = OpCodes.Call; call.operand = AccessTools.Method(typeof(TeleportAnywhere),"IsSourceBenchIcon");
                codes[i] = call; safeIcon = true;
            }
        }
        if(!context || !safeIcon)throw new InvalidOperationException("Unsupported native map help IL.");
        return codes;
    }
    static bool HasMapTravelContext(NelChipBench bench)
    { return bench != null || NativeMapTravelReady(); }
    static bool IsSourceBenchIcon(NelChipBench bench, WMIcon icon)
    { return bench != null && bench.IconIs(icon); }
    static bool BeforeMapTravelCommand(object __instance, XX.aBtn B, ref bool __result)
    {
        if(Instance == null || !Instance.isActiveAndEnabled || B == null || B.title != "&&Btn_move")return true;
        var skin = MapSkin.GetValue(__instance) as ButtonSkinWholeMapArea;
        if(skin == null || !IsMenuMap(skin))return true;
        string reason = Instance.TravelBlockReason(true);
        if(reason == null && !NativeMapTravelReady())reason = "当前剧情、危险度或天气不允许传送。";
        if(reason == null)return true;
        B.SetChecked(false);
        Instance.Tell(reason,6);
        __result = false;
        return false;
    }
    static void AfterMapTravelCommand(XX.aBtn B)
    {
        // Move is a command, not a toggle. Native code checks it before knowing
        // whether the request succeeded, so clear it even when a prefix rejects.
        if(Instance != null && Instance.isActiveAndEnabled && B != null && B.title == "&&Btn_move")B.SetChecked(false);
    }
    static void AfterNativeEligibility(UiGameMenu __instance)
    {
        if(Instance == null || !Instance.isActiveAndEnabled || World == null || World.GM != __instance)return;
        // Map2d.can_handle is the previous simulation tick's copy of this event
        // flag. Opening the menu immediately after a transfer pauses that tick,
        // leaving the old false value cached for the whole menu session.
        CanFastTravel.SetValue(__instance, (bool)PlayerOnBench.GetValue(__instance) &&
            __instance.BenchChip != null && SCN.isBenchCmdEnable("fast_travel") && !evt.EV.isStoppingGameHandle());
    }
    static void AfterNativeMapAppear(object __instance)
    {
        var skin = MapSkin.GetValue(__instance) as ButtonSkinWholeMapArea;
        if(skin == null || !IsMenuMap(skin))return;
        World.GM.recheckCanFastTravel();
        bool ready = NativeMapTravelReady();
        var controller = MapController.GetValue(__instance) as UiWmSkinController;
        if(controller != null)controller.can_use_fasttravel = ready;
        // Vanilla only sets these fields when creating the skin. Cached map
        // pages must instead start from this opening's bench/event state.
        skin.FastTravelFocused = default(WMIconPosition);
        skin.fast_travel_active = ready && skin.is_detail;
        if(skin.fast_travel_active)BenchList.SetValue(skin, KnownBenches(skin.getWholeMapTarget()));
        skin.fine_flag = skin.redraw_marker = true;
        Instance.Logger.LogDebug("Native map refreshed: bench="+(World.GM.BenchChip != null)+" travel="+ready);
    }
    static bool IsMenuMap(ButtonSkinWholeMapArea skin)
    {
        var w = World;
        if(Instance == null || !Instance.isActiveAndEnabled || w == null || w.GM == null || !w.GM.isActive())return false;
        var pages = MapPages.GetValue(w.GM) as IList;
        return pages != null && pages.Count > 5 && pages[5] != null && MapSkin.GetValue(pages[5]) == skin;
    }
    static bool NativeMapTravelReady()
    {
        var w = World;
        if(Instance == null || !Instance.isActiveAndEnabled || w == null || w.GM == null || !w.GM.isActive())return false;
        var pages = MapPages.GetValue(w.GM) as IList;
        // This plugin owns off-bench access and confirmation itself. Reading
        // the effective permission also respects other native-map extensions.
        return pages != null && pages.Count > 5 && pages[5] != null &&
            (bool)EffectiveMapTravel.GetValue(pages[5], null) &&
            SCN.isBenchCmdEnable("fast_travel") && !evt.EV.isStoppingGameHandle();
    }
    static List<WMIconPosition> KnownBenches(WholeMapItem area)
    {
        var list = area.getNoticedIconList(WMIcon.TYPE.BENCH);
        list.RemoveAll(delegate(WMIconPosition icon) {
            var map = icon.getDepertureMap();
            // Native noticed icons already represent discovered benches. Indoor
            // benches can be drawn on a different outer map that is not visited.
            return !icon.valid || map == null || map.is_whole;
        });
        return list;
    }
    static bool SameBench(WMIconPosition a, WMIconPosition b)
    {
        // Vanilla isSame compares a hidden-departure struct with the whole
        // position struct, so it cannot reliably identify the same bench.
        return a.valid && b.valid && a.get_Icon() == b.get_Icon() && a.getDepertureMap() == b.getDepertureMap() &&
            a.getDepertureMapPos() == b.getDepertureMapPos();
    }
    static void AfterNativeMapTarget(ButtonSkinWholeMapArea __instance)
    {
        if(!IsMenuMap(__instance) || !NativeMapTravelReady() || !__instance.is_detail)return;
        // The vanilla region switch clears foreign benches and disables travel.
        // Rebuild from the displayed region, not the player's current region.
        __instance.FastTravelFocused = default(WMIconPosition);
        __instance.fast_travel_active = true;
        BenchList.SetValue(__instance, KnownBenches(__instance.getWholeMapTarget()));
        __instance.fine_flag = __instance.redraw_marker = true;
    }
    static void AfterNativeTravelMode(ButtonSkinWholeMapArea __instance)
    {
        if(!IsMenuMap(__instance) || !NativeMapTravelReady() || !__instance.fast_travel_active)return;
        var area = __instance.getWholeMapTarget();
        if(area != null)BenchList.SetValue(__instance, KnownBenches(area));
    }
    static bool BeforeNativeTravel(WMIconPosition Dest, WholeMapItem SrcWM, ref bool __result)
    {
        var w = World;
        if(Instance == null || !Instance.isActiveAndEnabled || SrcWM != null || !Dest.valid || w == null)return true;
        var map = Dest.getDepertureMap();
        if(map == null || w.WM.GetWholeFor(map,true) == w.WM.CurWM)return true;
        __result = false;
        if(!NativeMapTravelReady())return false;
        var area = w.WM.GetWholeFor(map,true);
        if(area == null || !KnownBenches(area).Exists(delegate(WMIconPosition icon) { return SameBench(icon,Dest); }))return false;
        var position = Dest.getDepertureMapPos();
        __result = Instance.BeginTravelCore(new Destination { MapKey=map.key, X=(int)position.x, Y=(int)position.y,
            Name=MapName(map), Area=area.localized_name, Kind="已发现长椅" }, true);
        return false;
    }
    void OpenNativeMap()
    {
        var w = World;
        if(w == null || w.GM == null)return;
        if(w.GM.isActive())
        {
            if(AccessTools.Field(typeof(UiGameMenu),"edit_categ").GetValue(w.GM).ToString() == "MAP")
                w.GM.deactivate();
            else w.GM.activateMap();
            return;
        }
        string reason = BlockReason();
        if(reason != null){Tell(reason,5); return;}
        w.GM.activateMap();
    }
}
