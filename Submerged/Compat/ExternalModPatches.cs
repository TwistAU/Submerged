using System;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Submerged.Extensions;
using UnityEngine;

//Adds Compatability with Mods.

namespace Submerged.Compat;

[HarmonyPatch]
public static class ExternalModPatches
{
    private const string DivaniGuid = "com.divani.mods";

    private static bool _applied;
    private static PropertyInfo _portal1Object;
    private static PropertyInfo _portal2Object;

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
    [HarmonyPostfix]
    public static void ApplyExternalPatches()
    {
        if (_applied) return;
        _applied = true;

        TryPatchDivaniPortalVisibility();
        TryFixEscapistMarker();
        TryArmWikiButtonFix();
        TryDiagnoseImitator();
        TryDiagnoseImitatorTrigger();
    }

    private static bool _wikiArmed;
    private static FieldInfo _wikiButtonField;
    private static MethodInfo _setUpButtonPositions;
    private static int _wikiCooldown;

    private static void TryArmWikiButtonFix()
    {
        Type hudPatches = FindTypeInPlugins("TownOfUs.Patches.HudManagerPatches");
        Type localSettings = FindTypeInPlugins("TownOfUs.TownOfUsLocalSettings");
        if (hudPatches == null || localSettings == null) return;

        _wikiButtonField = AccessTools.Field(hudPatches, "WikiButton");
        _setUpButtonPositions = AccessTools.Method(localSettings, "SetUpButtonPositions");
        if (_wikiButtonField != null && _setUpButtonPositions != null)
        {
            _wikiArmed = true;
            Warning("[Compat] Wiki button anchor fix armed.");
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    [HarmonyPostfix]
    public static void WikiAnchorUpdate()
    {
        if (!_wikiArmed) return;
        if (_wikiCooldown > 0) { _wikiCooldown--; return; }

        try
        {
            if (_wikiButtonField.GetValue(null) is GameObject wiki && wiki && wiki.transform.parent == null)
            {
                _setUpButtonPositions.Invoke(null, null);
                _wikiCooldown = 30;
            }
        }
        catch
        {
            _wikiArmed = false;
        }
    }

    private static PropertyInfo _escapeMarkProp;
    private static FieldInfo _escapeMarkField;

    private static void TryFixEscapistMarker()
    {
        Type escType = FindTypeInPlugins("TownOfUs.Roles.Impostor.EscapistRole");
        if (escType == null) return;
        try
        {
            _escapeMarkProp = AccessTools.Property(escType, "EscapeMark");
            _escapeMarkField = AccessTools.Field(escType, "EscapeMark");
            new Harmony("submerged.compat.escapist").Patch(
                AccessTools.Method(escType, "RpcMarkLocation"),
                postfix: new HarmonyMethod(typeof(ExternalModPatches), nameof(EscapistMarkPostfix)));
            Warning("[Compat] Patched Escapist mark visibility.");
        }
        catch (Exception e)
        {
            Error($"[Compat] Escapist patch failed: {e}");
        }
    }

    public static void EscapistMarkPostfix(PlayerControl player, Vector2 pos)
    {
        if (!ShipStatus.Instance || !ShipStatus.Instance.IsSubmerged()) return;

        var role = player?.Data?.Role;
        if (role == null) return;

        object markObj = _escapeMarkProp != null ? _escapeMarkProp.GetValue(role) : _escapeMarkField?.GetValue(role);
        if (markObj is not GameObject mark || !mark) return;

        Vector3 lp = mark.transform.localPosition;
        lp.z = (pos.y + 0.3f) / 1000f;
        mark.transform.localPosition = lp;
    }

    private static void TryDiagnoseImitatorTrigger()
    {
        Type evType = FindTypeInPlugins("TownOfUs.Events.Crewmate.ImitatorEvents");
        if (evType == null) return;
        try
        {
            new Harmony("submerged.compat.imitrigger").Patch(
                AccessTools.Method(evType, "RoundStartEventHandler"),
                prefix: new HarmonyMethod(typeof(ExternalModPatches), nameof(ImiTriggerDiag)));
            Warning("[ImiDiag] Imitator round-start trigger diagnostics attached.");
        }
        catch (Exception e)
        {
            Error($"[ImiDiag] trigger attach failed: {e}");
        }
    }

    public static void ImiTriggerDiag(object __0)
    {
        try
        {
            object tbi = AccessTools.Property(__0.GetType(), "TriggeredByIntro")?.GetValue(__0);
            Warning($"[ImiDiag] RoundStartHandler ENTER triggeredByIntro={tbi}");
        }
        catch (Exception e)
        {
            Warning($"[ImiDiag] trigger read err: {e.Message}");
        }
    }

    private static Type FindTypeInPlugins(string fullName)
    {
        foreach (var plugin in IL2CPPChainloader.Instance.Plugins.Values)
        {
            try
            {
                Type t = plugin.Instance.GetType().Assembly.GetType(fullName);
                if (t != null) return t;
            }
            catch
            {
            }
        }
        return null;
    }

    private static FieldInfo _imiSelectedPlr;
    private static MethodInfo _imiUpdateRole;
    private static PropertyInfo _imiPlayerProp;
    private static object _localImitatorMod;

    private static void TryDiagnoseImitator()
    {
        Type imiType = FindTypeInPlugins("TownOfUs.Modifiers.Crewmate.ImitatorCacheModifier");
        if (imiType == null) return;

        try
        {
            _imiSelectedPlr = AccessTools.Field(imiType, "_selectedPlr");
            _imiUpdateRole = AccessTools.Method(imiType, "UpdateRole");
            _imiPlayerProp = AccessTools.Property(imiType, "Player");
            var harmony = new Harmony("submerged.compat.imidiag");
            var self = typeof(ExternalModPatches);

            harmony.Patch(AccessTools.Method(imiType, "Click"),
                postfix: new HarmonyMethod(self, nameof(ImiClickDiag)));
            harmony.Patch(AccessTools.Method(imiType, "UpdateRole"),
                prefix: new HarmonyMethod(self, nameof(ImiUpdateRoleDiag)));
            harmony.Patch(AccessTools.Method(imiType, "OnDeactivate"),
                prefix: new HarmonyMethod(self, nameof(ImiDeactivateDiag)));
            harmony.Patch(AccessTools.Method(imiType, "OnMeetingStart"),
                prefix: new HarmonyMethod(self, nameof(ImiMeetingStartDiag)));

            Warning("[ImiDiag] Imitator diagnostics attached.");
        }
        catch (Exception e)
        {
            Error($"[ImiDiag] attach failed: {e}");
        }
    }

    private static string Describe(object instance)
    {
        try
        {
            object sel = _imiSelectedPlr?.GetValue(instance);
            if (sel is NetworkedPlayerInfo npi)
            {
                return $"selected={npi.PlayerName} dead={npi.IsDead} disc={npi.Disconnected} obj={(npi.Object != null)}";
            }
            return "selected=null";
        }
        catch (Exception e)
        {
            return $"selected=<err:{e.Message}>";
        }
    }

    public static void ImiClickDiag(object __instance) { CacheIfLocalImitator(__instance); Warning($"[ImiDiag] Click -> {Describe(__instance)}"); }

    public static void ImiUpdateRoleDiag(object __instance) { CacheIfLocalImitator(__instance); Warning($"[ImiDiag] UpdateRole ENTER {Describe(__instance)}"); }

    public static void ImiDeactivateDiag(object __instance) => Warning($"[ImiDiag] OnDeactivate (will null selection) {Describe(__instance)}");

    public static void ImiMeetingStartDiag(object __instance) { CacheIfLocalImitator(__instance); Warning($"[ImiDiag] OnMeetingStart {Describe(__instance)}"); }

    private static void CacheIfLocalImitator(object instance)
    {
        try
        {
            if (_imiPlayerProp?.GetValue(instance) is PlayerControl p && p && p.AmOwner)
            {
                _localImitatorMod = instance;
            }
        }
        catch
        {
        }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Close))]
    [HarmonyPostfix]
    public static void DriveImitatorAfterMeeting()
    {
        if (_localImitatorMod == null || _imiUpdateRole == null) return;
        try
        {
            _imiUpdateRole.Invoke(_localImitatorMod, null);
            Warning("[ImiFix] Drove UpdateRole on meeting close.");
        }
        catch (Exception e)
        {
            Warning($"[ImiFix] drive failed: {e.Message}");
        }
    }

    private static void TryPatchDivaniPortalVisibility()
    {
        if (!IL2CPPChainloader.Instance.Plugins.TryGetValue(DivaniGuid, out var plugin)) return;

        try
        {
            Assembly asm = plugin.Instance.GetType().Assembly;
            Type portalManager = asm.GetType("DivaniMods.Buttons.Crewmate.CrewmateSupport.PortalManager");
            if (portalManager == null) return;

            MethodInfo createVisual = AccessTools.Method(portalManager, "CreatePortalVisual");
            if (createVisual == null) return;

            _portal1Object = AccessTools.Property(portalManager, "Portal1Object");
            _portal2Object = AccessTools.Property(portalManager, "Portal2Object");

            new Harmony("submerged.compat.divani").Patch(createVisual,
                postfix: new HarmonyMethod(typeof(ExternalModPatches), nameof(PortalVisualPostfix)));

            Warning("[Compat] Patched Divani Portalmaker portal visibility.");
        }
        catch (Exception e)
        {
            Error($"[Compat] Failed to patch Divani portals: {e}");
        }
    }

    public static void PortalVisualPostfix(Vector2 __0, int __1)
    {
        PropertyInfo prop = __1 == 1 ? _portal1Object : _portal2Object;
        if (prop?.GetValue(null) is not GameObject portal || !portal) return;

        Vector3 pos = portal.transform.position;
        pos.z = __0.y / 1000f;
        portal.transform.position = pos;
    }
}
