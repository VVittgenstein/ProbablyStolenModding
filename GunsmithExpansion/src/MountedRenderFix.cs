using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace GunsmithExpansion
{
    // Fixes vanilla GameItemElement.ValidateSelectLocked omitting the minus sign on modifiedYOrigin when setting
    // background and fakeBackground anchoredPosition (Y is down in cell coordinates and up in Unity UI coordinates).
    // Vanilla placed background at (+modifiedXOrigin * gridSize, +modifiedYOrigin * gridSize) instead of
    // (+modifiedXOrigin * gridSize, -modifiedYOrigin * gridSize), shifting guns with top-mounted attachments (cy < 0,
    // modifiedYOrigin > 0) upward by 2 * modifiedYOrigin * gridSize in local space across all 8 (orientation, flipped)
    // states and during drag preview.
    //
    // Because UpdateModSprite and UpdateInnateModSprite position active spriteMods, fakeSpriteMods, innateSpriteMods
    // and innateFakeSpriteMods relative to background.rectTransform.anchoredPosition during ValidateSelectLocked, this
    // Postfix computes the exact deltaY = expectedY - actualY (making the fix idempotent if vanilla ever corrects the
    // sign) and applies that same deltaY to background, fakeBackground and every active sprite-modifier node.
    [HarmonyPatch(typeof(GameItemElement), "ValidateSelectLocked", new[] { typeof(bool) })]
    internal static class MountedRenderFix
    {
        private static void Postfix(
            GameItemElement __instance,
            List<TreeNodeRender> ___spriteMods,
            List<TreeNodeRender> ___fakeSpriteMods,
            List<TreeNodeRender> ___innateSpriteMods,
            List<TreeNodeRender> ___innateFakeSpriteMods)
        {
            try
            {
                if (__instance == null || __instance.modifiedYOrigin == 0 || UISettings.current == null)
                    return;

                TreeNodeRender bg = __instance.background;
                if (bg == null || bg.rectTransform == null)
                    return;

                int gridSize = UISettings.current.gridSize;
                if (gridSize <= 0)
                    return;

                Vector2 bgPos = bg.rectTransform.anchoredPosition;
                float expectedY = -__instance.modifiedYOrigin * gridSize;
                float deltaY = expectedY - bgPos.y;
                if (Mathf.Approximately(deltaY, 0f))
                    return;

                bg.rectTransform.anchoredPosition = new Vector2(bgPos.x, expectedY);

                TreeNodeRender fakeBg = __instance.fakeBackground;
                if (fakeBg != null && fakeBg.rectTransform != null)
                {
                    Vector2 fakePos = fakeBg.rectTransform.anchoredPosition;
                    fakeBg.rectTransform.anchoredPosition = new Vector2(fakePos.x, expectedY);
                }

                ShiftActiveNodes(___spriteMods, deltaY);
                ShiftActiveNodes(___fakeSpriteMods, deltaY);
                ShiftActiveNodes(___innateSpriteMods, deltaY);
                ShiftActiveNodes(___innateFakeSpriteMods, deltaY);
            }
            catch (Exception e)
            {
                GunsmithMod.Warn("MountedRenderFix: " + e.Message);
            }
        }

        private static void ShiftActiveNodes(List<TreeNodeRender> nodes, float deltaY)
        {
            if (nodes == null)
                return;
            for (int i = 0; i < nodes.Count; i++)
            {
                TreeNodeRender node = nodes[i];
                if (node == null || node.gameObject == null || !node.gameObject.activeSelf || node.rectTransform == null)
                    continue;
                Vector2 pos = node.rectTransform.anchoredPosition;
                node.rectTransform.anchoredPosition = new Vector2(pos.x, pos.y + deltaY);
            }
        }
    }
}
