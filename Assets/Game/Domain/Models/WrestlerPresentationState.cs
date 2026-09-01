using System;
using System.Collections.Generic;

namespace PWManager.Domain.Models
{
    [Serializable]
    public sealed class WrestlerPresentationState
    {
        public MatchArchetype MatchArchetype;
        public PromoArchetype PromoArchetype;
        public PromoDisposition PromoDisposition;
        public string WrestlingStyleId;
        public string PortraitResourcePath;
        public List<string> TraitIds = new();
        public List<string> SignatureMoveIds = new();
        public List<string> FinisherMoveIds = new();
    }
}
