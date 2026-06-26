using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Reactor.Utilities;
using Submerged.Elevators.Objects;
using Submerged.Extensions;
using Submerged.Floors;
using Submerged.SpawnIn;
using UnityEngine;

//Compatibility for external mods n stuff

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
        TryFixImitator();
        TryArmCooldownPause();
        TryFixConjurerRock();
        TryFixMinerVents();
        TryFixDuelistWalls();
        TryFixCrossModCrashes();
        TryFixMediumGuess();
        TryFixGuesserRoles();
        TryGuardMissingResources();
        TryFixGamesNotEnding();
        TryArmWikiFocusGuard();
    }

    private static void TryFixGamesNotEnding()
    {
        Type flowPatches = FindTypeInPlugins("TownOfUs.Patches.LogicGameFlowPatches");
        if (flowPatches == null) return;

        try
        {
            MethodInfo check = AccessTools.Method(flowPatches, "CheckEndCriteriaPatch");
            if (check == null) return;

            new Harmony("submerged.compat.gameend").Patch(
                check, finalizer: new HarmonyMethod(typeof(ExternalModPatches), nameof(CheckEndCriteriaFallback)));
        }
        catch (Exception e)
        {
            Error($"[Compat] Game-end fallback failed: {e}");
        }
    }

    public static Exception CheckEndCriteriaFallback(Exception __exception, ref bool __result)
    {
        if (__exception == null) return null;
        __result = true;
        return null;
    }

    private static void TryGuardMissingResources()
    {
        try
        {
            Type spriteTools = FindTypeInPlugins("MiraAPI.Utilities.Assets.SpriteTools");
            if (spriteTools == null) return;

            MethodInfo loadTex = AccessTools.Method(spriteTools, "LoadTextureFromResourcePath");
            if (loadTex != null)
            {
                new Harmony("submerged.compat.spritetex").Patch(
                    loadTex, finalizer: new HarmonyMethod(typeof(ExternalModPatches), nameof(SpriteLoadFinalizer)));
            }
        }
        catch (Exception e)
        {
            Error($"[Compat] Missing-resource guard failed: {e}");
        }
    }

    public static Exception SpriteLoadFinalizer(Exception __exception, ref Texture2D __result)
    {
        if (__exception == null) return null;
        try { __result = new Texture2D(2, 2); } catch { }
        return null;
    }

    private static void TryFixMediumGuess()
    {
        Type assassinType = FindTypeInPlugins("TownOfUs.Modifiers.Game.AssassinModifier");
        if (assassinType == null) return;

        try
        {
            MethodInfo m = AccessTools.Method(assassinType, "IsRoleValid");
            if (m == null) return;

            new Harmony("submerged.compat.mediumguess").Patch(
                m, postfix: new HarmonyMethod(typeof(ExternalModPatches), nameof(MediumGuessPostfix)));
        }
        catch (Exception e)
        {
            Error($"[Compat] Medium guess patch failed: {e}");
        }
    }

    public static void MediumGuessPostfix(RoleBehaviour __0, ref bool __result)
    {
        if (__result || __0 == null) return;

        try
        {
            if (__0.GetIl2CppType().FullName == "TownOfUs.Roles.Crewmate.MediumRole") __result = true;
        }
        catch
        {
        }
    }

    private static bool _guesserBuilding;
    private static bool _roleOptsHooked;

    private static void TryFixGuesserRoles()
    {
        Type assassinType = FindTypeInPlugins("TownOfUs.Modifiers.Game.AssassinModifier");
        if (assassinType == null) return;

        var self = typeof(ExternalModPatches);

        try
        {
            MethodInfo click = AccessTools.Method(assassinType, "ClickGuess");
            if (click != null)
            {
                new Harmony("submerged.compat.guesserroles").Patch(click,
                    prefix: new HarmonyMethod(self, nameof(GuesserBuildStart)),
                    finalizer: new HarmonyMethod(self, nameof(GuesserBuildEnd)));
            }
        }
        catch (Exception e)
        {
            Error($"[Compat] Guesser ClickGuess patch failed: {e.Message}");
        }

        TryHookRoleOptions();
    }

    public static void GuesserBuildStart() => _guesserBuilding = true;

    public static Exception GuesserBuildEnd(Exception __exception)
    {
        _guesserBuilding = false;
        return __exception;
    }

    private static void TryHookRoleOptions()
    {
        if (_roleOptsHooked) return;

        var self = typeof(ExternalModPatches);
        var harmony = new Harmony("submerged.compat.roleopts");
        int hooked = 0;

        foreach (string typeName in new[]
                 {
                     "RoleOptionsCollectionV10", "RoleOptionsCollectionV09",
                     "RoleOptionsCollectionV08", "RoleOptionsCollectionV07"
                 })
        {
            try
            {
                Type t = AccessTools.TypeByName(typeName);
                if (t == null) continue;

                MethodInfo num = AccessTools.Method(t, "GetNumPerGame");
                MethodInfo chance = AccessTools.Method(t, "GetChancePerGame");
                if (num != null) { harmony.Patch(num, postfix: new HarmonyMethod(self, nameof(RoleNumPostfix))); hooked++; }
                if (chance != null) { harmony.Patch(chance, postfix: new HarmonyMethod(self, nameof(RoleChancePostfix))); hooked++; }
            }
            catch (Exception e)
            {
                Error($"[Compat] role-opts {typeName}: {e.Message}");
            }
        }

        _roleOptsHooked = hooked > 0;
    }

    public static void RoleNumPostfix(ref int __result)
    {
        if (_guesserBuilding && __result <= 0) __result = 1;
    }

    public static void RoleChancePostfix(ref int __result)
    {
        if (_guesserBuilding && __result <= 0) __result = 100;
    }

    private static void TryFixCrossModCrashes()
    {
        Type retType = FindTypeInPlugins("DivaniMods.Patches.RetributionistCursePatches");
        if (retType != null)
        {
            try
            {
                MethodInfo m = AccessTools.Method(retType, "ForceDisableButtonsPostfix");
                if (m != null)
                {
                    new Harmony("submerged.compat.retcurse").Patch(
                        m, prefix: new HarmonyMethod(typeof(ExternalModPatches), nameof(RetCurseGuardPrefix)));
                }
            }
            catch (Exception e)
            {
                Error($"[Compat] Retributionist guard failed: {e}");
            }
        }

        Type vincType = FindTypeInPlugins("TownOfExtra.Events.VinculatorEvents");
        if (vincType != null)
        {
            try
            {
                MethodInfo m = AccessTools.Method(vincType, "EjectionEventHandler");
                if (m != null)
                {
                    new Harmony("submerged.compat.vinculator").Patch(
                        m, finalizer: new HarmonyMethod(typeof(ExternalModPatches), nameof(VinculatorEjectionFinalizer)));
                }
            }
            catch (Exception e)
            {
                Error($"[Compat] Vinculator guard failed: {e}");
            }
        }
    }

    public static bool RetCurseGuardPrefix() => PlayerControl.LocalPlayer != null;

    public static Exception VinculatorEjectionFinalizer(Exception __exception) => null;

    private static PropertyInfo _minerVentSpriteProp;
    private static MethodInfo _loadAssetMethod;
    private static readonly Dictionary<byte, List<Vent>> _minerVentChains = new();
    private static int _minerVentShip = int.MinValue;

    private static void TryFixMinerVents()
    {
        Type minerType = FindTypeInPlugins("TownOfUs.Roles.Impostor.MinerRole");
        if (minerType == null) return;

        try
        {
            MethodInfo place = AccessTools.Method(minerType, "RpcPlaceVent");
            if (place == null) return;

            Type touAssets = FindTypeInPlugins("TownOfUs.Assets.TouAssets");
            if (touAssets != null)
            {
                _minerVentSpriteProp = AccessTools.Property(touAssets, "MinerVentSprite");
                if (_minerVentSpriteProp != null)
                {
                    _loadAssetMethod = AccessTools.Method(_minerVentSpriteProp.PropertyType, "LoadAsset");
                }
            }

            new Harmony("submerged.compat.miner").Patch(
                place,
                prefix: new HarmonyMethod(typeof(ExternalModPatches), nameof(MinerPlaceVentPrefix)));
        }
        catch (Exception e)
        {
            Error($"[Compat] Miner vent patch failed: {e}");
        }
    }

    public static bool MinerPlaceVentPrefix(PlayerControl player, int ventId, Vector2 position, float zAxis, bool immediate)
    {
        if (!ShipStatus.Instance || !ShipStatus.Instance.IsSubmerged()) return true;
        if (LobbyBehaviour.Instance) return true;
        if (!player) return true;

        try
        {
            Vent ventPrefab = ShipStatus.Instance.AllVents[0];
            Vent vent = UnityObject.Instantiate(ventPrefab, ventPrefab.transform.parent);
            vent.EnterVentAnim = null;
            vent.ExitVentAnim = null;

            if (vent.myAnim)
            {
                vent.transform.localScale = new Vector3(0.9f, 0.9f, 1);
                BoxCollider2D collider = vent.GetComponent<BoxCollider2D>();
                if (collider)
                {
                    collider.size = new Vector2(0.75f, 0.34f);
                    collider.offset = new Vector2(-0.005f, 0);
                }
                vent.Offset = new Vector3(0, 0.15f, 0);
                vent.myAnim.Stop();
                UnityObject.Destroy(vent.myAnim);
                vent.myAnim = null;
            }

            vent.numFramesUntilPlayerDisappearsOnEnter = 0;
            vent.numFramesUntilPlayerReappearsOnExit = 0;

            if (vent.myRend)
            {
                Sprite sprite = LoadMinerSprite();
                if (sprite) vent.myRend.sprite = sprite;
            }

            vent.name = $"MinerVent-{player.PlayerId}-{ventId}";

            if (!player.AmOwner && !immediate)
            {
                vent.gameObject.SetActive(false);
            }

            vent.Id = ventId;
            vent.transform.position = new Vector3(position.x, position.y, zAxis + 0.001f);

            int shipId = ShipStatus.Instance.GetInstanceID();
            if (shipId != _minerVentShip)
            {
                _minerVentChains.Clear();
                _minerVentShip = shipId;
            }
            if (!_minerVentChains.TryGetValue(player.PlayerId, out List<Vent> chain))
            {
                chain = new List<Vent>();
                _minerVentChains[player.PlayerId] = chain;
            }
            chain.RemoveAll(x => !x);

            if (chain.Count > 0)
            {
                Vent leftVent = chain[chain.Count - 1];
                vent.Left = leftVent;
                leftVent.Right = vent;
            }
            else
            {
                vent.Left = null;
            }
            vent.Right = null;
            vent.Center = null;
            chain.Add(vent);

            List<Vent> list = new();
            foreach (Vent v in ShipStatus.Instance.AllVents) list.Add(v);
            list.Add(vent);
            ShipStatus.Instance.AllVents = list.ToArray();

            vent.gameObject.layer = 12;
            vent.gameObject.AddComponent<ElevatorMover>();
            float ventZ = position.y > -7f ? position.y / 1000f + 0.005f : position.y / 1000f + 0.02f;
            vent.transform.position = new Vector3(position.x, position.y, ventZ);
        }
        catch (Exception e)
        {
            Error($"[Compat] Miner vent placement failed: {e}");
        }

        return false;
    }

    private static Sprite LoadMinerSprite()
    {
        try
        {
            object loadable = _minerVentSpriteProp?.GetValue(null);
            if (loadable == null || _loadAssetMethod == null) return null;
            return _loadAssetMethod.Invoke(loadable, null) as Sprite;
        }
        catch
        {
            return null;
        }
    }

    private static void TryFixDuelistWalls()
    {
        Type duelRpc = FindTypeInPlugins("DivaniMods.Networking.Neutral.NeutralOutlier.DuelistRpc");
        Type duelMgr = FindTypeInPlugins("DivaniMods.Modules.Duelist.DuelManager");
        if (duelRpc == null && duelMgr == null) return;

        try
        {
            if (duelRpc != null)
            {
                MethodInfo start = AccessTools.Method(duelRpc, "RpcStartDuel");
                if (start != null)
                {
                    new Harmony("submerged.compat.duelist").Patch(
                        start,
                        postfix: new HarmonyMethod(typeof(ExternalModPatches), nameof(DuelStartPostfix)));
                }
            }

            if (duelMgr != null)
            {
                MethodInfo dests = AccessTools.Method(duelMgr, "TryGetDuelDestinations");
                if (dests != null)
                {
                    new Harmony("submerged.compat.dueldest").Patch(
                        dests,
                        postfix: new HarmonyMethod(typeof(ExternalModPatches), nameof(DuelDestPostfix)));
                }
            }
        }
        catch (Exception e)
        {
            Error($"[Compat] Duelist patch failed: {e}");
        }
    }

    public static void DuelDestPostfix(PlayerControl duelist, PlayerControl target, ref Vector2 duelistDest, ref Vector2 targetDest, ref bool __result)
    {
        if (!ShipStatus.Instance || !ShipStatus.Instance.IsSubmerged()) return;
        if (!duelist || !target) return;

        var vents = ShipStatus.Instance.AllVents;
        if (vents != null && vents.Length >= 2)
        {
            int i = UnityRandom.Range(0, vents.Length);
            int j = UnityRandom.Range(0, vents.Length);
            int guard = 0;
            while (j == i && guard++ < 16) j = UnityRandom.Range(0, vents.Length);

            Vent va = vents[i];
            Vent vb = vents[j];
            if (va && vb)
            {
                duelistDest = (Vector2) va.transform.position;
                targetDest = (Vector2) vb.transform.position;
                __result = true;
                return;
            }
        }

        duelistDest = duelist.GetTruePosition();
        targetDest = target.GetTruePosition();
        __result = true;
    }

    public static void DuelStartPostfix(PlayerControl duelist, byte targetId, Vector2 duelistDest, Vector2 targetDest)
    {
        if (!ShipStatus.Instance || !ShipStatus.Instance.IsSubmerged()) return;

        PlayerControl local = PlayerControl.LocalPlayer;
        if (!local) return;

        Vector2 dest;
        if (duelist && duelist.AmOwner) dest = duelistDest;
        else if (local.PlayerId == targetId) dest = targetDest;
        else return;

        Coroutines.Start(CoFixDuelFloor(dest));
    }

    private static IEnumerator CoFixDuelFloor(Vector2 dest)
    {
        yield return null;
        yield return null;

        if (!ShipStatus.Instance || !ShipStatus.Instance.IsSubmerged()) yield break;

        PlayerControl local = PlayerControl.LocalPlayer;
        if (!local) yield break;

        bool destUpper = dest.y > FloorHandler.FLOOR_CUTOFF;
        FloorHandler handler = FloorHandler.GetFloorHandler(local);
        if (!handler || handler.onUpper == destUpper) yield break;

        handler.RegisterFloorOverride(destUpper);
        handler.RpcRequestChangeFloor(destUpper);
        local.transform.position = new Vector3(dest.x, dest.y, dest.y / 1000f);
        if (local.NetTransform) local.NetTransform.SnapTo(dest);
        if (HudManager.Instance) HudManager.Instance.PlayerCam.SnapToTarget();
    }

    private static void TryArmWikiFocusGuard()
    {
        try
        {
            MethodInfo giveFocus = AccessTools.Method(typeof(TextBoxTMP), "GiveFocus");
            if (giveFocus == null) return;

            new Harmony("submerged.compat.givefocus").Patch(
                giveFocus,
                prefix: new HarmonyMethod(typeof(ExternalModPatches), nameof(GiveFocusSafePrefix)),
                finalizer: new HarmonyMethod(typeof(ExternalModPatches), nameof(GiveFocusFinalizer)));
        }
        catch (Exception e)
        {
            Error($"[Compat] GiveFocus guard failed: {e}");
        }
    }

    public static bool GiveFocusSafePrefix(TextBoxTMP __instance)
    {
        try
        {
            Minigame mg = Minigame.Instance;
            if (!mg) return true;

            string typeName = mg.GetIl2CppType().FullName;
            if (string.IsNullOrEmpty(typeName) || (!typeName.Contains("Wiki") && !typeName.Contains("Guesser")))
            {
                return true;
            }

            __instance.hasFocus = true;
            return false;
        }
        catch
        {
            return true;
        }
    }

    public static Exception GiveFocusFinalizer(Exception __exception) => null;

    private static MethodInfo _miraButtonUpdate;

    private static void TryArmCooldownPause()
    {
        Type pcPatches = FindTypeInPlugins("MiraAPI.Patches.PlayerControlPatches");
        if (pcPatches == null) return;

        try
        {
            _miraButtonUpdate = AccessTools.Method(pcPatches, "PlayerControlFixedUpdatePostfix");
            if (_miraButtonUpdate == null) return;

            new Harmony("submerged.compat.cooldownpause").Patch(
                _miraButtonUpdate,
                prefix: new HarmonyMethod(typeof(ExternalModPatches), nameof(MiraButtonFreezePrefix)));
        }
        catch (Exception e)
        {
            Error($"[Compat] Cooldown pause patch failed: {e}");
        }
    }

    public static bool MiraButtonFreezePrefix()
    {
        Minigame mg = Minigame.Instance;
        if (!mg) return true;
        if (mg.TryCast<SubmarineSelectSpawn>() == null) return true;
        return false;
    }

    private static FieldInfo _conjurerRockField;
    private static Il2CppSystem.Type _squashedBodyCppType;

    private static void TryFixConjurerRock()
    {
        Type rpcsType = FindTypeInPlugins("TownOfExtra.Networking.ConjurerRpcs");
        if (rpcsType == null) return;

        Type squashedBody = FindTypeInPlugins("TownOfExtra.Networking.SquashedBody");
        if (squashedBody != null)
        {
            try { _squashedBodyCppType = Il2CppType.From(squashedBody); } catch { }
        }

        try
        {
            Type iter = null;
            foreach (Type nested in rpcsType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nested.Name.Contains("SpawnRock")) { iter = nested; break; }
            }
            if (iter == null) return;

            MethodInfo moveNext = AccessTools.Method(iter, "MoveNext");
            if (moveNext == null) return;

            foreach (FieldInfo f in iter.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.FieldType != typeof(GameObject)) continue;
                _conjurerRockField = f;
                if (f.Name.Contains("rock")) break;
            }
            if (_conjurerRockField == null) return;

            new Harmony("submerged.compat.conjurer").Patch(
                moveNext,
                postfix: new HarmonyMethod(typeof(ExternalModPatches), nameof(ConjurerRockMoveNextPostfix)));
        }
        catch (Exception e)
        {
            Error($"[Compat] Conjurer rock patch failed: {e}");
        }
    }

    public static void ConjurerRockMoveNextPostfix(object __instance)
    {
        if (!ShipStatus.Instance || !ShipStatus.Instance.IsSubmerged()) return;

        try
        {
            if (_conjurerRockField.GetValue(__instance) is GameObject rock && rock)
            {
                Vector3 pos = rock.transform.position;
                pos.z = pos.y / 1000f;
                rock.transform.position = pos;
            }

            FixSquashedBodies();
        }
        catch
        {
        }
    }

    private static void FixSquashedBodies()
    {
        if (_squashedBodyCppType == null) return;

        try
        {
            foreach (UnityObject obj in UnityObject.FindObjectsOfType(_squashedBodyCppType))
            {
                Component comp = obj.TryCast<Component>();
                if (comp == null || !comp) continue;

                Vector3 pos = comp.transform.position;
                if (pos.z <= pos.y / 1000f + 0.5f) continue;

                pos.z = pos.y / 1000f + 0.005f;
                comp.transform.position = pos;
            }
        }
        catch
        {
        }
    }

    private static bool _wikiArmed;
    private static FieldInfo _wikiButtonField;
    private static FieldInfo _zoomButtonField;
    private static MethodInfo _setUpButtonPositions;
    private static int _wikiCooldown;

    private static void TryArmWikiButtonFix()
    {
        Type hudPatches = FindTypeInPlugins("TownOfUs.Patches.HudManagerPatches");
        Type localSettings = FindTypeInPlugins("TownOfUs.TownOfUsLocalSettings");
        if (hudPatches == null || localSettings == null) return;

        _wikiButtonField = AccessTools.Field(hudPatches, "WikiButton");
        _zoomButtonField = AccessTools.Field(hudPatches, "ZoomButton");
        _setUpButtonPositions = AccessTools.Method(localSettings, "SetUpButtonPositions");
        if (_wikiButtonField != null && _setUpButtonPositions != null)
        {
            _wikiArmed = true;
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    [HarmonyPostfix]
    public static void WikiAnchorUpdate()
    {
        if (!_wikiArmed) return;

        try
        {
            if (ShipStatus.Instance && ShipStatus.Instance.IsSubmerged())
            {
                AnchorToHud(_wikiButtonField, 1.6f);
                AnchorToHud(_zoomButtonField, 2.4f);
                return;
            }

            if (_wikiCooldown > 0) { _wikiCooldown--; return; }

            if (!IsButtonOrphaned(_wikiButtonField) && !IsButtonOrphaned(_zoomButtonField)) return;

            _setUpButtonPositions.Invoke(null, null);
            _wikiCooldown = 15;
        }
        catch
        {
            _wikiArmed = false;
        }
    }

    private static bool IsButtonOrphaned(FieldInfo field)
    {
        return field?.GetValue(null) is GameObject go && go && go.transform.parent == null;
    }

    private static void AnchorToHud(FieldInfo field, float yOffset)
    {
        if (field?.GetValue(null) is not GameObject go || !go) return;
        if (!HudManager.Instance || !HudManager.Instance.MapButton) return;

        Transform map = HudManager.Instance.MapButton.transform;
        AspectPosition mapAspect = map.GetComponent<AspectPosition>();
        if (!mapAspect) return;

        if (go.transform.parent != map.parent) go.transform.SetParent(map.parent, false);
        go.transform.localScale = map.localScale;

        AspectPosition aspect = go.GetComponent<AspectPosition>();
        if (!aspect) aspect = go.AddComponent<AspectPosition>();

        aspect.Alignment = mapAspect.Alignment;
        aspect.DistanceFromEdge = mapAspect.DistanceFromEdge + new Vector3(0f, yOffset, 0f);
        aspect.AdjustPosition();
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

    private static MethodInfo _imiUpdateRole;
    private static PropertyInfo _imiPlayerProp;
    private static object _localImitatorMod;

    private static void TryFixImitator()
    {
        Type imiType = FindTypeInPlugins("TownOfUs.Modifiers.Crewmate.ImitatorCacheModifier");
        if (imiType == null) return;

        try
        {
            _imiUpdateRole = AccessTools.Method(imiType, "UpdateRole");
            _imiPlayerProp = AccessTools.Property(imiType, "Player");

            var harmony = new Harmony("submerged.compat.imitator");
            var self = typeof(ExternalModPatches);
            harmony.Patch(AccessTools.Method(imiType, "Click"),
                postfix: new HarmonyMethod(self, nameof(CacheImitatorPostfix)));
            harmony.Patch(AccessTools.Method(imiType, "OnMeetingStart"),
                postfix: new HarmonyMethod(self, nameof(CacheImitatorPostfix)));
        }
        catch (Exception e)
        {
            Error($"[Compat] Imitator patch failed: {e}");
        }
    }

    public static void CacheImitatorPostfix(object __instance)
    {
        try
        {
            if (_imiPlayerProp?.GetValue(__instance) is PlayerControl p && p && p.AmOwner)
            {
                _localImitatorMod = __instance;
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
        }
        catch
        {
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
