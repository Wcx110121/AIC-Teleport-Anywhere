using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public sealed partial class TeleportAnywhere
{
    bool releaseInput, keyHeld, escapeHeld;
    int neutralInputFrame = -1, hotkeyFrame = -1;
    static readonly FieldInfo NativeKeys = AccessTools.Field(typeof(XX.IN), "KA");
    static readonly FieldInfo StickLevels = AccessTools.Field(typeof(XX.KEY), "Apad_tilt_level");
    static readonly FieldInfo NativeMouse = AccessTools.Field(typeof(XX.IN), "mvMouse");
    bool CapturingInput { get { return show || releaseInput; } }
    void InstallInput()
    {
        harmony.Patch(AccessTools.Method(typeof(XX.IN), "run"), prefix: new HarmonyMethod(typeof(TeleportAnywhere), "BeforeNativeInput"));
        harmony.Patch(AccessTools.Method(typeof(XX.KEY), "updatePressing"), prefix: new HarmonyMethod(typeof(TeleportAnywhere), "BeforeNativeKeys"));
        foreach(MethodInfo m in typeof(XX.IN).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            if(m.Name == "getKD" || m.Name == "getK" || m.Name == "getKU" || m.Name == "isMousePushDown" ||
                m.Name == "isMouseOn" || m.Name == "isMouseUp" || m.Name == "isSubmitOrMouseUp")
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(TeleportAnywhere), "BeforeNativeButton"));
        harmony.Patch(AccessTools.Method(typeof(XX.KEY), "isReturnPD"), prefix: new HarmonyMethod(typeof(TeleportAnywhere), "BeforeNativeButton"));
    }
    void PollInput()
    {
        Keyboard kb = Keyboard.current;
        bool down = kb != null && hotkey.Value != Key.None && kb[hotkey.Value].isPressed;
        bool escape = kb != null && kb.escapeKey.isPressed;
        if(hotkeyFrame != Time.frameCount)
        {
            if(down && !keyHeld) { if(extraPanel.Value)SetVisible(!show); else OpenNativeMap(); hotkeyFrame = Time.frameCount; }
            else if(escape && !escapeHeld && show) { SetVisible(false); hotkeyFrame = Time.frameCount; }
        }
        keyHeld = down; escapeHeld = escape;
        if(!releaseInput || show)return;
        if(ControlsHeld())neutralInputFrame = -1;
        else if(neutralInputFrame < 0)neutralInputFrame = Time.frameCount;
        else if(Time.frameCount > neutralInputFrame) { ClearNativeInput(null); releaseInput = false; }
    }
    static bool ControlsHeld()
    {
        if(Keyboard.current != null && Keyboard.current.anyKey.isPressed)return true;
        Mouse m = Mouse.current;
        if(m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed))return true;
        foreach(Gamepad pad in Gamepad.all)
        {
            if(pad.leftStick.ReadValue().sqrMagnitude > .04f || pad.rightStick.ReadValue().sqrMagnitude > .04f)return true;
            foreach(var c in pad.allControls) { ButtonControl b = c as ButtonControl; if(b != null && b.isPressed)return true; }
        }
        return false;
    }
    static void ClearNativeInput(XX.KEY keys)
    {
        if(keys == null)keys = (XX.KEY)NativeKeys.GetValue(null);
        if(keys == null)return;
        XX.IN.clearPushDown(true); keys.clearPress(); keys.mvCHECK = keys.mvRUN = 0;
        float[] tilt = (float[])StickLevels.GetValue(keys);
        if(tilt != null)Array.Clear(tilt, 0, tilt.Length);
        NativeMouse.SetValue(null, 0f);
    }
    static void BeforeNativeInput()
    {
        if(Instance == null)return; Instance.PollInput();
        if(Instance.CapturingInput)ClearNativeInput(null);
    }
    static bool BeforeNativeKeys(XX.KEY __instance)
    {
        if(Instance == null)return true; Instance.PollInput();
        if(!Instance.CapturingInput)return true;
        ClearNativeInput(__instance); return false;
    }
    static bool BeforeNativeButton(ref bool __result)
    {
        if(Instance == null || !Instance.CapturingInput)return true;
        __result = false; return false;
    }
}
