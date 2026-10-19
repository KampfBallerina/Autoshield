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
        static ConfigEntry<bool> showTooltip;
        static int equipDepth; // >0 while Humanoid.EquipItem runs, so weapon swaps are not mistaken for unequipping
        static Plugin instance;
        static readonly HashSet<string> disabled = new HashSet<string>();
        static readonly System.Reflection.FieldInfo elementsField = AccessTools.Field(typeof(InventoryGrid), "m_elements");
        static readonly System.Reflection.FieldInfo hotbarElementsField = AccessTools.Field(typeof(HotkeyBar), "m_elements");
        static readonly System.Reflection.FieldInfo hotbarGoField = AccessTools.Field(AccessTools.Inner(typeof(HotkeyBar), "ElementData"), "m_go");
        static HotkeyBar hotbar;
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
            showTooltip = Config.Bind("General", "ShowTooltipText", true,
                "Add the [Auto Shield] line to the item description. If false, only the border marks the shield.");
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
            if (p == null) return;
            var marked = FindMarked(p);
            if (marked == null) return;

            if (InventoryGui.instance != null && InventoryGui.IsVisible())
            {
                var grid = InventoryGui.instance.m_playerGrid;
                var elements = elementsField.GetValue(grid) as List<InventoryElement>;
                int idx = marked.m_gridPos.y * (int)widthField.GetValue(grid) + marked.m_gridPos.x;
                if (elements != null && idx >= 0 && idx < elements.Count && elements[idx] != null)
                    DrawSparks(elements[idx].GetElementRectTransform());
            }

            // Hotbar = first inventory row; element index equals the grid column.
            if (marked.m_gridPos.y == 0 && hotbar != null && hotbar.gameObject.activeInHierarchy
                && hotbarElementsField.GetValue(hotbar) is System.Collections.IList slots
                && marked.m_gridPos.x >= 0 && marked.m_gridPos.x < slots.Count && slots[marked.m_gridPos.x] != null)
                DrawSparks(hotbarGoField.GetValue(slots[marked.m_gridPos.x]) is GameObject go ? go.transform as RectTransform : null);
        }

        // Two slim gold sparks with long fading tails circle the slot, opposite each other.
        static void DrawSparks(RectTransform rt)
        {
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

            var center = (corners[0] + corners[2]) / 2f;
            var lens = new float[4];
            float perimeter = 0f;
            for (int i = 0; i < 4; i++)
            {
                corners[i] += (center - corners[i]).normalized * 1.5f; // slightly inset
                perimeter += lens[i] = (corners[(i + 1) % 4] - corners[i]).magnitude;
            }
            if (!(perimeter > 0f && perimeter < 2000f)) return; // NaN or absurd rect: draw nothing

            const int tail = 40;
            const float step = 2f, speed = 50f; // px between tail dots, px per second
            for (int spark = 0; spark < 2; spark++)
                for (int j = 0; j < tail; j++)
                {
                    float d = ((Time.unscaledTime * speed + spark * perimeter / 2f - j * step) % perimeter + perimeter) % perimeter;
                    int edge = 0;
                    while (edge < 3 && d > lens[edge]) d -= lens[edge++];
                    var pt = Vector3.Lerp(corners[edge], corners[(edge + 1) % 4], lens[edge] > 0f ? d / lens[edge] : 0f);
                    float fade = 1f - (float)j / tail;
                    GUI.color = new Color(1f, 0.82f, 0.45f, 0.9f * fade * fade);
                    const float size = 2f;
                    GUI.DrawTexture(new Rect(pt.x - size / 2f, pt.y - size / 2f, size, size), Texture2D.whiteTexture);
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

        // Remember the hotbar so the border can be drawn on it too.
        [HarmonyPatch(typeof(HotkeyBar), "UpdateIcons")]
        static class HotbarSeen
        {
            static void Postfix(HotkeyBar __instance) => hotbar = __instance;
        }

        // A dropped shield must not carry the mark into the world.
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.DropItem))]
        static class OnDrop
        {
            static void Prefix(ItemDrop.ItemData item) => Safe("drop", () => item?.m_customData?.Remove(Key));
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), new[] { typeof(int) })]
        static class Tooltip
        {
            static void Postfix(ItemDrop.ItemData __instance, ref string __result)
            {
                try { if (showTooltip.Value && __instance.m_customData != null && IsMarked(__instance)) __result += "\n<color=orange>[Auto Shield]</color>"; }
                catch (System.Exception) { } // a tooltip must never throw
            }
        }
    }
}
