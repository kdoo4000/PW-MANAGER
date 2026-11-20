using System;

namespace PWManager.Domain.Models
{
    public enum BeltStrapColor { Black, White, Brown }
    public enum BeltMetalColor { Gold, RoseGold, Silver }
    public enum BeltLettering { Ornate, Engraved, Embossed }

    [Serializable]
    public sealed class BeltDesignState
    {
        public int Template = 1;
        public int MainEmblem = 3;
        public int SideEmblem = 1;
        public BeltStrapColor StrapColor;
        public BeltMetalColor MetalColor;
        public bool Nameplates;
        public string NameplateText = "";
        public string Abbreviation = "";
        public bool OverlayAbbreviation;
        public bool MainGems;
        public bool SideGems;
        public BeltLettering Lettering;

        public BeltDesignState Copy() => (BeltDesignState)MemberwiseClone();
    }
}
