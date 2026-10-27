using UnityEngine;
using UnityEngine.UI;

namespace FlapGame.GameLib.Extensions
{
    /// <summary>uGUI: CanvasGroup, Graphic (Image, RawImage, Text, TextMeshProUGUI), RectTransform.</summary>
    public static class UIExtensions
    {
        /// <summary>Shows (alpha 1, clickable) or hides (alpha 0, lets clicks through) the group without deactivating it.</summary>
        public static void SetVisible(this CanvasGroup group, bool visible)
        {
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        /// <summary>Turns clicks on or off for the group and everything under it; the alpha stays.</summary>
        public static void SetInteractable(this CanvasGroup group, bool interactable)
        {
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
        }

        /// <summary>Changes only the alpha of an Image, Text, TextMeshProUGUI or any other Graphic.</summary>
        public static void SetAlpha(this Graphic graphic, float alpha)
        {
            Color color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }

        // The size setters write sizeDelta: they size the rect only when its anchors are together (not stretched).

        /// <summary>Width <paramref name="width"/>, height from <paramref name="aspect"/> (height / width).</summary>
        public static void SetSizeByWidth(this RectTransform rect, float width, float aspect) =>
            rect.sizeDelta = new Vector2(width, width * aspect);

        /// <summary>Height <paramref name="height"/>, width from <paramref name="aspect"/> (height / width).</summary>
        public static void SetSizeByHeight(this RectTransform rect, float height, float aspect) =>
            rect.sizeDelta = new Vector2(aspect > 0f ? height / aspect : 0f, height);

        /// <summary>Width <paramref name="width"/>, height that keeps the sprite's proportions. Does nothing without a sprite.</summary>
        public static void SetSizeByWidth(this Image image, float width)
        {
            if (TryGetSpriteAspect(image, out float aspect)) image.rectTransform.SetSizeByWidth(width, aspect);
        }

        /// <summary>Height <paramref name="height"/>, width that keeps the sprite's proportions. Does nothing without a sprite.</summary>
        public static void SetSizeByHeight(this Image image, float height)
        {
            if (TryGetSpriteAspect(image, out float aspect)) image.rectTransform.SetSizeByHeight(height, aspect);
        }

        private static bool TryGetSpriteAspect(Image image, out float aspect)
        {
            Sprite sprite = image.sprite;
            if (sprite == null || sprite.rect.width <= 0f)
            {
                aspect = 0f;
                return false;
            }
            aspect = sprite.rect.height / sprite.rect.width;
            return true;
        }
    }
}
