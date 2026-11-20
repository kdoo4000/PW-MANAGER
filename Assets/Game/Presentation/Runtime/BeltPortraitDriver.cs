using UnityEngine;

namespace PWManager.Presentation
{
    [ExecuteAlways]
    public sealed class BeltPortraitDriver : MonoBehaviour
    {
        private void LateUpdate() => ChampionPortraitRenderer.FlushPrepared();
    }
}
