using Avalonia.Controls;
using Avalonia.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace UMB.CLI.Views
{
    public partial class TrackOrderWindow : Window
    {
        public List<TrackViewModel> Result { get; private set; }

        private readonly ObservableCollection<TrackViewModel> _tracks = new();

        public TrackOrderWindow()
        {
            InitializeComponent();
        }

        public TrackOrderWindow(List<TrackViewModel> items) : this()
        {
            foreach (var item in items)
                _tracks.Add(item);
            UpdateOrderDisplay();
            TrackItemsControl.ItemsSource = _tracks;

            AddHandler(DragDrop.DropEvent, OnDrop);
            AddHandler(DragDrop.DragOverEvent, OnDragOver);
        }

        private async void OnItemPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (sender is Border border && border.DataContext is TrackViewModel vm)
            {
                var data = new DataObject();
                data.Set("TrackItem", vm);
                await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.Move;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.Contains("TrackItem"))
                return;

            var draggedItem = e.Data.Get("TrackItem") as TrackViewModel;
            if (draggedItem == null)
                return;

            var pos = e.GetPosition(TrackItemsControl);
            // Each row is approximately 52px tall (padding + content)
            int targetIndex = Math.Clamp((int)(pos.Y / 52), 0, _tracks.Count - 1);

            int currentIndex = _tracks.IndexOf(draggedItem);
            if (currentIndex >= 0 && currentIndex != targetIndex)
            {
                _tracks.Move(currentIndex, targetIndex);
                UpdateOrderDisplay();
            }
        }

        private void UpdateOrderDisplay()
        {
            for (int i = 0; i < _tracks.Count; i++)
                _tracks[i].OrderDisplay = $"#{i}";
        }

        private void OnSave(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Result = _tracks.ToList();
            Close();
        }

        private void OnCancel(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Result = null;
            Close();
        }
    }

    public class TrackViewModel : INotifyPropertyChanged
    {
        public int OriginalIndex { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public bool IsVanilla { get; set; }
        public string BgmId { get; set; }
        public Avalonia.Media.FontStyle TitleFontStyle => IsVanilla ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal;

        private string _orderDisplay;
        public string OrderDisplay
        {
            get => _orderDisplay;
            set
            {
                if (_orderDisplay != value)
                {
                    _orderDisplay = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
