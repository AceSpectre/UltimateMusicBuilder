using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Sma5h.CLI.Views
{
    public partial class SeriesOrderWindow : Window
    {
        public List<string> Result { get; private set; }

        private readonly ObservableCollection<SeriesViewModel> _series = new();

        public SeriesOrderWindow()
        {
            InitializeComponent();
        }

        public SeriesOrderWindow(List<SeriesViewModel> items) : this()
        {
            foreach (var item in items)
                _series.Add(item);
            SeriesItemsControl.ItemsSource = _series;

            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }

        private async void OnItemPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is SeriesViewModel vm)
            {
                var data = new DataObject();
                data.Set("SeriesItem", vm);
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.Move;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.Contains("SeriesItem"))
                return;

            var draggedItem = e.Data.Get("SeriesItem") as SeriesViewModel;
            if (draggedItem == null)
                return;

            var pos = e.GetPosition(SeriesItemsControl);
            int col = Math.Clamp((int)(pos.X / 128), 0, 4);
            int row = Math.Max(0, (int)(pos.Y / 148));
            int targetIndex = Math.Clamp(row * 5 + col, 0, _series.Count - 1);

            int currentIndex = _series.IndexOf(draggedItem);
            if (currentIndex >= 0 && currentIndex != targetIndex)
            {
                _series.Move(currentIndex, targetIndex);
            }
        }

        private void OnSave(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Result = _series.Select(s => s.Id).ToList();
            Close();
        }

        private void OnCancel(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Result = null;
            Close();
        }
    }

    public class SeriesViewModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string IconPath { get; set; }

        private Bitmap _iconBitmap;
        public Bitmap IconBitmap => _iconBitmap ??= LoadIcon();

        private Bitmap LoadIcon()
        {
            if (!string.IsNullOrEmpty(IconPath) && System.IO.File.Exists(IconPath))
            {
                try { return new Bitmap(IconPath); }
                catch { return null; }
            }
            return null;
        }
    }
}
