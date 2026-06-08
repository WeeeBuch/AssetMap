using System;
using System.IO;
using System.Threading.Tasks;
using AssetMap.Avalonia.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

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

    // ── Import CSV ────────────────────────────────────────────
    private async void ImportCsv_Click(object? sender, RoutedEventArgs e)
    {
        var vm = DataContext as AccountsViewModel;
        if (vm?.SelectedAccount is null) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Vyberte CSV soubor",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CSV soubor") { Patterns = ["*.csv"] },
                new FilePickerFileType("Všechny soubory")  { Patterns = ["*.*"] },
            ],
        });

        if (files.Count == 0) return;

        await using var stream = await files[0].OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        await vm.RunImportAsync(vm.SelectedAccount.AccountId, files[0].Name, ms.ToArray());
    }
}
