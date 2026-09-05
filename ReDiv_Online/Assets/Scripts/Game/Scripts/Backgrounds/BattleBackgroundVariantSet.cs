using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Holds the complete battle backgrounds that can occupy one battle scene.
/// A quest selects one background ID per wave, so every variant shares the
/// same world origin and only one variant is active at a time.
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleBackgroundVariantSet : MonoBehaviour
{
    [Serializable]
    public sealed class Variant
    {
        [SerializeField] private string backgroundId;
        [SerializeField] private GameObject root;

        public string BackgroundId => backgroundId;
        public GameObject Root => root;

        public Variant(string backgroundId, GameObject root)
        {
            this.backgroundId = backgroundId;
            this.root = root;
        }
    }

    [SerializeField] private List<Variant> variants = new();
    [SerializeField, Min(0)] private int initialVariantIndex;
    [SerializeField, HideInInspector] private int activeVariantIndex = -1;

    public IReadOnlyList<Variant> Variants => variants;
    public int ActiveVariantIndex => activeVariantIndex;
    public string ActiveBackgroundId =>
        activeVariantIndex >= 0 && activeVariantIndex < variants.Count
            ? variants[activeVariantIndex].BackgroundId
            : string.Empty;

    private void Awake()
    {
        ShowByIndex(initialVariantIndex);
    }

    public bool ShowById(string backgroundId)
    {
        int index = variants.FindIndex(item =>
            string.Equals(item.BackgroundId, backgroundId, StringComparison.Ordinal));
        return index >= 0 && ShowByIndex(index);
    }

    public bool ShowByIndex(int index)
    {
        if (index < 0 || index >= variants.Count)
            return false;

        for (int itemIndex = 0; itemIndex < variants.Count; itemIndex++)
        {
            GameObject root = variants[itemIndex]?.Root;
            if (root != null)
                root.SetActive(itemIndex == index);
        }

        activeVariantIndex = index;
        return true;
    }

    public void Configure(IReadOnlyList<string> backgroundIds, IReadOnlyList<GameObject> roots, int initialIndex)
    {
        if (backgroundIds == null)
            throw new ArgumentNullException(nameof(backgroundIds));
        if (roots == null)
            throw new ArgumentNullException(nameof(roots));
        if (backgroundIds.Count != roots.Count)
            throw new ArgumentException("Background ID count must match variant root count.");

        variants.Clear();
        for (int index = 0; index < backgroundIds.Count; index++)
            variants.Add(new Variant(backgroundIds[index], roots[index]));

        initialVariantIndex = variants.Count == 0
            ? 0
            : Mathf.Clamp(initialIndex, 0, variants.Count - 1);
        activeVariantIndex = -1;
        if (variants.Count > 0)
            ShowByIndex(initialVariantIndex);
    }
}
