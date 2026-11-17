using System;
using System.Collections.Generic;
using ProbablyStolen.DigitalStorage.Client.Interaction.Drag;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolen.DigitalStorage.Platform.Client.Drag;

internal sealed class DragGhostView : IDisposable
{
    private readonly RectTransform _parent;
    private readonly RectTransform _root;
    private readonly RectTransform _view;
    private readonly Image _icon;
    private readonly TextMeshProUGUI _amount;
    private readonly CanvasGroup _group;
    private readonly ItemPictureCapture _pictures;
    private readonly List<Image> _layers = new();
    private readonly List<DragGhostView> _children = new();
    private readonly List<DragGhostOffset> _offsets = new();
    private readonly float _size;

    internal DragGhostView(RectTransform parent, ItemPictureCapture pictures, float size)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _pictures = pictures ?? throw new ArgumentNullException(nameof(pictures));
        _size = size;
        var root = new GameObject("drag_ghost", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.AddComponent<LayoutElement>().ignoreLayout = true;
        _group = root.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
        _root = (RectTransform)root.transform;
        _root.sizeDelta = new Vector2(size, size);
        _icon = new GameObject("drag_icon", typeof(RectTransform)).AddComponent<Image>();
        _icon.transform.SetParent(root.transform, false);
        _icon.preserveAspect = true;
        _icon.raycastTarget = false;
        Stretch(_icon.rectTransform, 4f);
        _view = (RectTransform)new GameObject("drag_view", typeof(RectTransform)).transform;
        _view.SetParent(root.transform, false);
        Stretch(_view, 4f);
        _amount = new GameObject("drag_amount", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        _amount.transform.SetParent(root.transform, false);
        Stretch(_amount.rectTransform, 2f);
        _amount.alignment = TextAlignmentOptions.BottomRight;
        _amount.fontSize = 12f;
        _amount.raycastTarget = false;
        root.SetActive(false);
    }

    internal void Show(ItemPicture? picture, PointerPoint pointer, Camera? camera, string? amountText, float opacity)
    {
        foreach (var child in _children) child.Hide();
        _offsets.Clear();
        SetPicture(picture, amountText, opacity);
        Move(pointer, camera);
    }

    internal void BeginMultiple(IReadOnlyList<ItemPicture?> pictures, IReadOnlyList<DragGhostOffset> offsets)
    {
        _offsets.Clear();
        _offsets.AddRange(offsets);
        while (_children.Count < pictures.Count) _children.Add(new DragGhostView(_root, _pictures, _size));
        for (var index = 0; index < _children.Count; index++)
        {
            if (index < pictures.Count) _children[index].SetPicture(pictures[index], null, 1f);
            else _children[index].Hide();
        }
        _icon.enabled = false;
        _view.gameObject.SetActive(false);
        _amount.enabled = false;
        _group.alpha = 0.8f;
    }

    internal void MoveMultiple(PointerPoint pointer, Camera? camera)
    {
        Move(pointer, camera);
        for (var index = 0; index < _offsets.Count; index++)
            _children[index].Move(_offsets[index].At(pointer), camera);
    }

    private void SetPicture(ItemPicture? picture, string? amountText, float opacity)
    {
        _view.gameObject.SetActive(true);
        _pictures.Show(_view, _layers, _icon, picture, true);
        _amount.text = amountText ?? string.Empty;
        _amount.enabled = amountText != null;
        _group.alpha = opacity;
    }

    private void Move(PointerPoint pointer, Camera? camera)
    {
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(_parent, new Vector2(pointer.X, pointer.Y), camera, out var world))
            _root.position = world;
    }

    internal void Hide()
    {
        _root.gameObject.SetActive(false);
        foreach (var child in _children) child.Hide();
        _offsets.Clear();
    }

    public void Dispose() => UnityEngine.Object.Destroy(_root.gameObject);

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
