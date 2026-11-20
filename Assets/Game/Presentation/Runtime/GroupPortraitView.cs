using System.Collections.Generic;
using System.Linq;
using PWManager.Domain.Models;
using UnityEngine;
using UnityEngine.UIElements;

namespace PWManager.Presentation
{
    public sealed class GroupPortraitView : VisualElement
    {
        public GroupPortraitView(IEnumerable<WrestlerState> members, string championshipId = null, string leaderId = null)
        {
            AddToClassList("group-portrait");
            styleSheets.Add(Resources.Load<StyleSheet>("PWManagerUI/GroupPortrait"));
            var people = members.Where(w => w != null).GroupBy(w => w.Id).Select(g => g.First()).ToList();
            var hasLeader = people.Any(w => w.Id == leaderId);
            if (hasLeader) people = people.OrderBy(w => w.Id == leaderId).ToList();
            var slots = Enumerable.Range(0, people.Count).ToArray();
            if (hasLeader) slots = slots.OrderByDescending(slot => slot == people.Count - 1 ? -1 : slot / 2).ToArray();
            foreach (var slot in slots)
            {
                var wrestler = people[slot];
                var picture = WrestlerProfileController.CreatePortraitLink(wrestler);
                picture.name = "group-portrait-" + wrestler.Id; picture.AddToClassList("group-portrait-member");
                picture.tooltip = WrestlerNameText.DisplayName(wrestler);
                if (championshipId != null && picture.Q<Image>() != null) picture.Q<Image>().image = PortraitRenderer.GetForChampionship(wrestler, championshipId);
                Add(picture);
            }
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                for (var i = 0; i < childCount; i++)
                {
                    var bounds = hasLeader ? StableMemberBounds(contentRect.width, contentRect.height, slots[i], childCount) : MemberBounds(contentRect.width, contentRect.height, i, childCount);
                    var picture = this[i]; picture.style.left = bounds.x; picture.style.top = bounds.y;
                    picture.style.width = bounds.width; picture.style.height = bounds.height;
                }
            });
        }

        internal static Rect MemberBounds(float width, float height, int index, int count)
        {
            var photoWidth = Mathf.Min(height * PortraitRenderer.Width / PortraitRenderer.Height, width / (1 + .38f * (count - 1)));
            var spacing = photoWidth * .38f;
            var center = width * .5f + (index - (count - 1) * .5f) * spacing;
            var photoHeight = photoWidth * PortraitRenderer.Height / PortraitRenderer.Width;
            return new Rect(center - photoWidth * .5f, height - photoHeight, photoWidth, photoHeight);
        }

        private static Rect StableMemberBounds(float width, float height, int index, int count)
        {
            if (count == 1) return MemberBounds(width, height, index, count);
            var sideCount = Mathf.CeilToInt((count - 1) / 2f);
            var farthest = sideCount - 1;
            var extent = .55f + farthest * .45f + .45f * Mathf.Pow(.86f, farthest);
            var portraitWidth = Mathf.Min(height * PortraitRenderer.Width / PortraitRenderer.Height, width / (2 * extent));
            var portraitHeight = portraitWidth * PortraitRenderer.Height / PortraitRenderer.Width;
            var top = height - portraitHeight;
            if (index == count - 1) return new Rect((width - portraitWidth) * .5f, top, portraitWidth, portraitHeight);
            var side = index % 2 == 0 ? -1 : 1; var position = index / 2;
            var center = width * .5f + side * portraitWidth * (.55f + position * .45f);
            var scale = .9f * Mathf.Pow(.86f, position);
            return new Rect(center - portraitWidth * scale * .5f, height - portraitHeight * scale, portraitWidth * scale, portraitHeight * scale);
        }
    }
}
