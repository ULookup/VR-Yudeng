using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishLantern.XR
{
    /// <summary>Serializable lookup entry for one reusable fish-lantern prefab.</summary>
    [Serializable]
    public sealed class InteractiveAssetCatalogEntry
    {
        [SerializeField] private string stableId;
        [SerializeField] private InteractableObjectType objectType;
        [SerializeField] private GameObject prefab;

        /// <summary>Stable semantic ID carried by the prefab root.</summary>
        public string StableId => stableId;

        /// <summary>Semantic prefab category.</summary>
        public InteractableObjectType ObjectType => objectType;

        /// <summary>Reusable prefab asset.</summary>
        public GameObject Prefab => prefab;

        /// <summary>Creates one catalog entry.</summary>
        public InteractiveAssetCatalogEntry(
            string id,
            InteractableObjectType type,
            GameObject prefabAsset)
        {
            stableId = id ?? string.Empty;
            objectType = type;
            prefab = prefabAsset;
        }
    }

    /// <summary>Central inventory of generated placeholders and replaceable model prefabs.</summary>
    [CreateAssetMenu(
        fileName = "FishLanternInteractiveAssetCatalog",
        menuName = "Fish Lantern XR/Interactive Asset Catalog")]
    public sealed class InteractiveAssetCatalog : ScriptableObject
    {
        [SerializeField] private List<InteractiveAssetCatalogEntry> entries = new();

        /// <summary>Read-only prefab catalog entries.</summary>
        public IReadOnlyList<InteractiveAssetCatalogEntry> Entries => entries;

        /// <summary>Replaces the complete catalog from deterministic editor tooling.</summary>
        public void Configure(IEnumerable<InteractiveAssetCatalogEntry> newEntries)
        {
            entries = newEntries == null
                ? new List<InteractiveAssetCatalogEntry>()
                : new List<InteractiveAssetCatalogEntry>(newEntries);
        }

        /// <summary>Finds the first prefab with a matching stable ID.</summary>
        public GameObject FindById(string stableId)
        {
            foreach (var entry in entries)
            {
                if (entry.StableId == stableId)
                    return entry.Prefab;
            }

            return null;
        }

        /// <summary>Finds the first prefab of a semantic type.</summary>
        public GameObject FindByType(InteractableObjectType objectType)
        {
            foreach (var entry in entries)
            {
                if (entry.ObjectType == objectType)
                    return entry.Prefab;
            }

            return null;
        }
    }
}
