using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Marks the small working end of a held tool, independently of its grab collider.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class ToolContactPoint : MonoBehaviour
    {
        private void Reset() => GetComponent<Collider>().isTrigger = true;
    }
}
