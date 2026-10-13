using System;

namespace PWManager.Domain.Models
{
    // Kayfabe relationship; unrelated to backstage friendship.
    [Serializable]
    public sealed class StoryRelationshipState
    {
        public string FirstWrestlerId, SecondWrestlerId;
        public float Value; // -100: enemies, 0: neutral, +100: allies
        public float Hostility; // Legacy saves only. Converted once by SaveService.
    }
}
