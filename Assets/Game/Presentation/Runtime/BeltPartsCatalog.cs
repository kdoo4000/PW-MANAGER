using System;
using PWManager.Domain.Models;
using UnityEngine;

namespace PWManager.Presentation
{
    [Serializable] internal sealed class BeltBox { public float x, y, width, height; }
    [Serializable] internal sealed class BeltPathPoint { public float x, y, height; }
    [Serializable] internal sealed class BeltGemPoint { public float x, y, angle, radius; }
    [Serializable] internal sealed class BeltGemRegion { public BeltGemPoint[] points; }
    [Serializable] internal sealed class BeltPlacement { public string id; public BeltBox[] plates; public BeltBox named; }
    [Serializable] internal sealed class BeltShield { public float x, y, left, right, inset; }
    [Serializable] internal sealed class BeltFold { public float x, slope; }
    [Serializable] internal sealed class BeltSeam { public float top, bottom; }
    [Serializable] internal sealed class BeltShoulderPose { public string gender; public int body; public bool left; public float x, y, scale, angle; }
    [Serializable] internal sealed class BeltTemplate
    {
        public string id, image, mask, fullImage, fullMask;
        public BeltFold fold;
        public BeltSeam seam;
        public BeltShoulderPose[] poses;
        public float width, height;
        public BeltBox crop, namedMain;
        public BeltBox[] boxes, plateRegions, nameplates;
        public BeltPathPoint[] stitchPath;
        public bool lowerTitle;
        public BeltShield shieldV;
        public BeltPlacement[] placements;
        public BeltGemRegion[] gems;
    }
    [Serializable] internal sealed class BeltOrnament { public string id, image; }
    [Serializable] internal sealed class BeltNameplateFrame { public string image, face; public BeltPathPoint[] path; public BeltBox box; }
    [Serializable] internal sealed class BeltNameplate { public string id; public BeltNameplateFrame[] frames; }
    [Serializable] internal sealed class BeltPartsCatalog
    {
        public BeltTemplate[] bases;
        public BeltOrnament[] ornaments;
        public BeltNameplate[] nameplates;
        private static BeltPartsCatalog instance;
        internal static BeltPartsCatalog Load() => instance ??= JsonUtility.FromJson<BeltPartsCatalog>(
            Resources.Load<TextAsset>("PWManagerUI/BeltParts/catalog").text);
    }
}
