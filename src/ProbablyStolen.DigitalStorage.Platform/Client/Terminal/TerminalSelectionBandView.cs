using System;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Terminal;

internal sealed class TerminalSelectionBandView : IDisposable
{
    private readonly RectTransform _parent;
    private readonly RectTransform _root;
    private readonly RectTransform[] _edges = new RectTransform[4];

    internal TerminalSelectionBandView(RectTransform parent)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _root = (RectTransform)new GameObject("terminal_selection_band", typeof(RectTransform)).transform;
        _root.SetParent(parent, false);
        _root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var group = _root.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        for (var i = 0; i < _edges.Length; i++)
        {
            var image = new GameObject("edge", typeof(RectTransform)).AddComponent<Image>();
            image.transform.SetParent(_root, false);
            image.raycastTarget = false;
            image.color = Color.white;
            _edges[i] = image.rectTransform;
        }
        Hide();
    }

    internal void Show(PointerRectangle bounds, Camera? camera)
    {
        if (!bounds.IsValid || !RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent,
                new Vector2(bounds.MinX, bounds.MinY), camera, out var min) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(_parent,
                new Vector2(bounds.MaxX, bounds.MaxY), camera, out var max))
        {
            Hide();
            return;
        }
        _root.anchorMin = _root.anchorMax = Vector2.zero;
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.anchoredPosition = (min + max) * 0.5f - _parent.rect.min;
        _root.sizeDelta = new Vector2(Mathf.Abs(max.x - min.x), Mathf.Abs(max.y - min.y));
        SetEdge(_edges[0], new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 2f));
        SetEdge(_edges[1], new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 2f));
        SetEdge(_edges[2], new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(2f, 0f));
        SetEdge(_edges[3], new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(2f, 0f));
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
    }

    internal void Hide() => _root.gameObject.SetActive(false);

    public void Dispose() => UnityEngine.Object.Destroy(_root.gameObject);

    private static void SetEdge(RectTransform edge, Vector2 min, Vector2 max, Vector2 size)
    {
        edge.anchorMin = min;
        edge.anchorMax = max;
        edge.pivot = new Vector2(0.5f, 0.5f);
        edge.anchoredPosition = Vector2.zero;
        edge.sizeDelta = size;
    }
}
