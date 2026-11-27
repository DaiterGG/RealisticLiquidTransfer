using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RealisticLiquidTransfer;

internal static class FunnelItem
{
    internal const string Id = "funnel";

    internal static Sprite Sprite => ModSprites.GetSprite("funnel-inventory");
    internal static Sprite TransferSprite => ModSprites.GetSprite(Id);

    internal static void Register()
    {
        if (Item.GlobalItems == null)
        {
            throw new InvalidOperationException("Game item definitions have not been initialized.");
        }

        if (Item.GlobalItems.TryGetValue(Id, out var existing))
        {
            if (existing.fullName != "Funnel")
            {
                throw new InvalidOperationException($"Item ID '{Id}' is already registered.");
            }

            return;
        }

        var funnelInfo = new ItemInfo
        {
            fullName = "Funnel",
            description = "A funnel for transferring liquids.",
            category = "custom",
            weight = 0.1f,
            value = 1,
            combineable = false
        };
        funnelInfo.SetTags();
        Item.GlobalItems.Add(Id, funnelInfo);
    }

    internal static void RegisterRecipe()
    {
        if (Recipes.recipes == null)
        {
            throw new InvalidOperationException("Game recipes have not been initialized.");
        }

        if (Recipes.recipes.Any(recipe => recipe.result?.id == Id))
        {
            return;
        }

        Recipes.recipes.Add(new Recipe
        {
            INT = 6,
            result = new RecipeResult { id = Id },
            items = new List<RecipeItem>
            {
                new RecipeItem(0f)
                {
                    specific = true,
                    specificId = "plasticchunk"
                },
                new RecipeItem(0f)
                {
                    quality = new CraftingQuality("cutting"),
                    minimumCondition = 0f,
                    destroyItem = false
                },
                new RecipeItem(10f)
                {
                    specific = true,
                    specificId = "biochem",
                    isLiquid = true
                }
            },
            category = Recipes.RecipeCategory.Tools
        });
    }

    internal static GameObject Create(Vector2 position, float rotation)
    {
        Register();

        var funnel = new GameObject(Id);
        funnel.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, rotation));
        var itemLayer = LayerMask.NameToLayer("Item");
        if (itemLayer >= 0)
        {
            funnel.layer = itemLayer;
        }

        var sprite = ModSprites.GetFunnelWorldSprite();
        funnel.AddComponent<SpriteRenderer>().sprite = sprite;
        funnel.AddComponent<Rigidbody2D>();

        var spriteBounds = sprite.bounds;
        var bowlCollider = funnel.AddComponent<BoxCollider2D>();
        bowlCollider.size = new Vector2(spriteBounds.size.x, spriteBounds.size.y * (5f / 9f));
        bowlCollider.offset = new Vector2(0f, spriteBounds.extents.y - bowlCollider.size.y * 0.5f);

        var stemCollider = funnel.AddComponent<BoxCollider2D>();
        stemCollider.size = new Vector2(spriteBounds.size.x * 0.25f, spriteBounds.size.y * (5f / 9f));
        stemCollider.offset = new Vector2(0f, -spriteBounds.extents.y + stemCollider.size.y * 0.5f);

        var item = funnel.AddComponent<Item>();
        item.id = Id;
        return funnel;
    }

    internal static GameObject Create(Transform parent)
    {
        var funnel = Create(parent.position, parent.eulerAngles.z);
        funnel.transform.SetParent(parent, true);
        return funnel;
    }
}

[HarmonyPatch(typeof(Item), nameof(Item.SetupItems))]
internal static class FunnelItemRegistrationPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        FunnelItem.Register();
    }
}

[HarmonyPatch(typeof(Recipes), nameof(Recipes.SetUpRecipes))]
internal static class FunnelRecipeRegistrationPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        FunnelItem.RegisterRecipe();
    }
}

[HarmonyPatch(typeof(Recipe), nameof(Recipe.resultSprite), MethodType.Getter)]
internal static class FunnelRecipeSpritePatch
{
    [HarmonyPrefix]
    private static bool Prefix(Recipe __instance, ref (Sprite, Color) __result)
    {
        if (__instance.result?.id != FunnelItem.Id)
        {
            return true;
        }

        __result = (FunnelItem.Sprite, Color.white);
        return false;
    }
}

[HarmonyPatch(typeof(Utils), nameof(Utils.Create), new[] { typeof(string), typeof(Vector2), typeof(float) })]
internal static class FunnelWorldCreationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string id, Vector2 pos, float rot, ref GameObject __result)
    {
        if (id != FunnelItem.Id)
        {
            return true;
        }

        __result = FunnelItem.Create(pos, rot);
        return false;
    }
}

[HarmonyPatch(typeof(Utils), nameof(Utils.Create), new[] { typeof(string), typeof(Transform) })]
internal static class FunnelParentedCreationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(string id, Transform trans, ref GameObject __result)
    {
        if (id != FunnelItem.Id)
        {
            return true;
        }

        __result = FunnelItem.Create(trans);
        return false;
    }
}
