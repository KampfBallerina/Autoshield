using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace Autoshield
{
    [BepInPlugin("KampfBallerina.autoshield", "Autoshield", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        const string Key = "KampfBallerina.autoshield";

        static ConfigEntry<KeyCode> markKey;
        static ConfigEntry<bool> stowWithWeapon;
        static int equipDepth; // >0 while Humanoid.EquipItem runs, so weapon swaps are not mistaken for unequipping
        static Plugin instance;
        static readonly HashSet<string> disabled = new HashSet<string>();
        static readonly System.Reflection.FieldInfo elementsField = AccessTools.Field(typeof(InventoryGrid), "m_elements");
        static readonly System.Reflection.FieldInfo widthField = AccessTools.Field(typeof(InventoryGrid), "m_width");

        // Runs a feature; on the first exception it logs once and switches that feature off for the session.
        static void Safe(string feature, System.Action action)
        {
            if (disabled.Contains(feature)) return;
            try { action(); }
            catch (System.Exception e)
            {
                disabled.Add(feature);
                instance.Logger.LogError("Autoshield feature '" + feature + "' disabled after error: " + e);
            }
        }

        void Awake()
        {
            instance = this;
            markKey = Config.Bind("General", "MarkKey", KeyCode.Mouse2,
                "Key or mouse button (e.g. Mouse2, Mouse3, F, LeftAlt) to hover over a shield in the inventory and mark it. None = middle mouse button.");
            stowWithWeapon = Config.Bind("General", "StowShieldWithWeapon", true,
                "Also put the marked shield away when the one-handed weapon is put away.");
            var h = new Harmony("KampfBallerina.autoshield");
            foreach (var t in typeof(Plugin).GetNestedTypes(System.Reflection.BindingFlags.NonPublic))
            {
                Logger.LogInfo("Patching " + t.Name);
                h.CreateClassProcessor(t).Patch();
            }
            Logger.LogInfo("Patched");
        }

        // Marching-ants border around the marked shield's inventory slot.
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            Safe("border", DrawBorder);
        }

        static void DrawBorder()
        {
            var p = Player.m_localPlayer;
            if (p == null || InventoryGui.instance == null || !InventoryGui.IsVisible()) return;
            var marked = FindMarked(p);
            if (marked == null) return;
            var grid = InventoryGui.instance.m_playerGrid;
            var elements = elementsField.GetValue(grid) as List<InventoryElement>;
            int idx = marked.m_gridPos.y * (int)widthField.GetValue(grid) + marked.m_gridPos.x;
            if (elements == null || idx < 0 || idx >= elements.Count || elements[idx] == null) return;

            var rt = elements[idx].GetElementRectTransform();
            var canvas = rt != null ? rt.GetComponentInParent<Canvas>() : null;
            if (canvas == null || !rt.gameObject.activeInHierarchy) return;
            var cam = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;

            var corners = new Vector3[4]; // bottom-left, top-left, top-right, bottom-right
            rt.GetWorldCorners(corners);
            for (int i = 0; i < 4; i++)
            {
                var sp = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                corners[i] = new Vector3(sp.x, Screen.height - sp.y);
            }

            // Valheim gold, slightly inset, with a soft pulse and a dark drop shadow.
            var gold = new Color(1f, 0.78f, 0.35f, 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 3f));
            var center = (corners[0] + corners[2]) / 2f;
            for (int i = 0; i < 4; i++) corners[i] += (center - corners[i]).normalized * 2f;
            float offset = Time.unscaledTime * 10f, pos = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % 4];
                float len = (b - a).magnitude;
                if (!(len > 0f && len < 500f)) return; // NaN or absurd rect: draw nothing
                int steps = Mathf.Min((int)(len / 2f), 250); // hard cap, the loop can never run away
                for (int s = 0; s < steps; s++, pos += 2f)
                    if ((pos + offset) % 10f < 6f)
                    {
                        var pt = Vector3.Lerp(a, b, s * 2f / len);
                        GUI.color = new Color(0f, 0f, 0f, 0.8f);
                        GUI.DrawTexture(new Rect(pt.x - 0.5f, pt.y - 0.5f, 3f, 3f), Texture2D.whiteTexture);
                        GUI.color = gold;
                        GUI.DrawTexture(new Rect(pt.x - 1.5f, pt.y - 1.5f, 3f, 3f), Texture2D.whiteTexture);
                    }
            }
            GUI.color = Color.white;
        }

        static bool IsMarked(ItemDrop.ItemData i) => i.m_customData.ContainsKey(Key);

        static ItemDrop.ItemData FindMarked(Player p)
        {
            foreach (var i in p.GetInventory().GetAllItems())
                if (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield && IsMarked(i)) return i;
            return null;
        }

        static void SetMark(Player p, ItemDrop.ItemData item)
        {
            foreach (var i in p.GetInventory().GetAllItems()) i.m_customData.Remove(Key);
            if (item != null) item.m_customData[Key] = "1";
        }

        // Mark key (default: middle click) on a shield in the inventory toggles the mark (only one shield is marked).
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        static class MiddleClick
        {
            static void Postfix(InventoryGui __instance) => Safe("mark", () =>
            {
                var p = Player.m_localPlayer;
                if (p == null || __instance.m_playerGrid == null || !Input.GetKeyDown(markKey.Value == KeyCode.None ? KeyCode.Mouse2 : markKey.Value) || !__instance.m_playerGrid.gameObject.activeInHierarchy) return;
                var m = Input.mousePosition;
                var item = __instance.m_playerGrid.GetItem(new Vector2i((int)m.x, (int)m.y));
                if (item == null || item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield) return;
                bool wasMarked = IsMarked(item);
                SetMark(p, wasMarked ? null : item);
                if (!wasMarked && p.RightItem != null && p.RightItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon)
                    p.EquipItem(item);
            });
        }

        // Weapon change: equip the marked shield for one-handed weapons, stow it otherwise.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        static class OnEquip
        {
            static void Prefix() => equipDepth++;
            static void Finalizer() => equipDepth--;

            static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result) => Safe("equip", () =>
            {
                if (item == null || !__result || __instance != Player.m_localPlayer) return;
                var p = (Player)__instance;
                var shield = FindMarked(p);
                if (shield == null) return;
                switch (item.m_shared.m_itemType)
                {
                    case ItemDrop.ItemData.ItemType.Shield:
                        if (item != shield) SetMark(p, item); // mark follows a manually equipped shield
                        break;
                    case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                        p.EquipItem(shield);
                        break;
                    case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                    case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                    case ItemDrop.ItemData.ItemType.Bow:
                    case ItemDrop.ItemData.ItemType.Torch:
                    case ItemDrop.ItemData.ItemType.Tool:
                        if (p.IsItemEquiped(shield)) p.UnequipItem(shield);
                        break;
                }
            });
        }

        // Weapon put away (not swapped): stow the marked shield too.
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        static class OnUnequip
        {
            static void Postfix(Humanoid __instance, ItemDrop.ItemData item) => Safe("unequip", () =>
            {
                if (!stowWithWeapon.Value || equipDepth > 0 || item == null || __instance != Player.m_localPlayer) return;
                if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.OneHandedWeapon) return;
                var p = (Player)__instance;
                var shield = FindMarked(p);
                if (shield != null && p.IsItemEquiped(shield)) p.UnequipItem(shield);
            });
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(int) })]
        static class Tooltip
        {
            static void Postfix(ItemDrop.ItemData __instance, ref string __result)
            {
                if (IsMarked(__instance)) __result += "\n<color=orange>[Auto-Schild]</color>";
            }
        }
    }
}
