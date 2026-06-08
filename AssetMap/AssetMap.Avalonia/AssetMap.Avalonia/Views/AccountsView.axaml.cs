using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace AssetMap.Avalonia.Views;

public partial class AccountsView : UserControl
{
    public AccountsView()
    {
        InitializeComponent();
    }

    // ── Drag-to-scroll ────────────────────────────────────────
    private const double DragThreshold = 6.0;

    private bool   _dragging;
    private bool   _hasDragged;
    private Point  _startPos;
    private double _startOffset;

    private void CardStrip_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(sender as Visual).Properties;
        if (!props.IsLeftButtonPressed) return;

        _dragging    = true;
        _hasDragged  = false;
        _startPos    = e.GetPosition(CardScroller);
        _startOffset = CardScroller.Offset.X;
    }

    private void CardStrip_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;

        double delta = _startPos.X - e.GetPosition(CardScroller).X;

        if (!_hasDragged)
        {
            if (Math.Abs(delta) < DragThreshold) return;

            _hasDragged = true;
            // Capture zde = karty nedostávají click, scroll ano
            e.Pointer.Capture(sender as IInputElement);
        }

        CardScroller.Offset = CardScroller.Offset.WithX(_startOffset + delta);
        e.Handled = true;
    }

    private void CardStrip_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_hasDragged)
        {
            e.Pointer.Capture(null);
            e.Handled = true;
        }
        _dragging   = false;
        _hasDragged = false;
    }

    private void CardStrip_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _dragging   = false;
        _hasDragged = false;
    }
}
