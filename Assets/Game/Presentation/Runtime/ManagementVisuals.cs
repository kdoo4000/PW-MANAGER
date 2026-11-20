using PWManager.Domain.Models;
using PWManager.Domain.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    internal static class ManagementVisuals
    {
        internal static void Style(VisualElement root)
        {
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/ManagementScreens"));
            root.styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/BeltCustomizer"));
            root.AddToClassList("mg-page");
        }

        internal static BeltPreview Belt(ChampionshipType type, bool world, string className = null, BeltDesignState design = null, string title = null)
        {
            var preview = new BeltPreview(BeltCustomizerView.Effective(design ?? BeltDesignRules.Default(type, world), title));
            preview.AddToClassList("mg-belt"); if (className != null) preview.AddToClassList(className); return preview;
        }

        internal static VisualElement Wrestler(WrestlerState wrestler, bool miniature = false, string championshipId = null)
        {
            var card = new VisualElement { name = "management-wrestler-" + wrestler.Id };
            card.AddToClassList(miniature ? "mg-miniature" : "mg-wrestler");
            var photo = WrestlerProfileController.CreatePortraitLink(wrestler); photo.AddToClassList("mg-photo");
            if (championshipId != null && photo.Q<Image>() != null) photo.Q<Image>().image = PortraitRenderer.GetForChampionship(wrestler, championshipId);
            photo.tooltip = WrestlerNameText.DisplayName(wrestler); card.Add(photo);
            if (!miniature) card.Add(WrestlerProfileController.CreateLink(wrestler));
            return card;
        }

        internal static void Stat(VisualElement parent, string label, string value)
        {
            var stat = new VisualElement(); stat.AddToClassList("mg-stat"); parent.Add(stat);
            var number = new Label(value); number.AddToClassList("mg-stat-value"); stat.Add(number);
            var caption = new Label(label); caption.AddToClassList("mg-caption"); stat.Add(caption);
        }
    }
}
