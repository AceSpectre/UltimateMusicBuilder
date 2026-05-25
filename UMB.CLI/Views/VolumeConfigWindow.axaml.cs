using Avalonia.Controls;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using UMB.CLI.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace UMB.CLI.Views
{
    public partial class VolumeConfigWindow : Window
    {
        public List<VolumeRowViewModel> Result { get; private set; }

        private readonly ObservableCollection<VolumeRowViewModel> _rows = new();
        private readonly AudioPreviewDecoder _decoder;

        // Single playback engine: only one row plays at a time.
        private IWavePlayer _player;
        private AudioFileReader _reader;
        private VolumeSampleProvider _volumeProvider;
        private VolumeRowViewModel _playingRow;

        public VolumeConfigWindow()
        {
            InitializeComponent();
        }

        public VolumeConfigWindow(List<VolumeRowViewModel> items, AudioPreviewDecoder decoder) : this()
        {
            _decoder = decoder;
            foreach (var item in items)
            {
                item.PropertyChanged += OnRowPropertyChanged;
                _rows.Add(item);
            }
            TrackItemsControl.ItemsSource = _rows;

            Closed += OnClosed;
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(VolumeRowViewModel.UserOverride)) return;
            // If the row whose override just changed is currently playing, update playback volume live.
            if (_playingRow != null && _volumeProvider != null && ReferenceEquals(sender, _playingRow))
            {
                _volumeProvider.Volume = _playingRow.EffectiveBankVolume;
            }
        }

        private void OnPlayClicked(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not VolumeRowViewModel vm)
                return;

            // Toggle: clicking the currently-playing row's button stops it.
            if (_playingRow != null && ReferenceEquals(_playingRow, vm))
            {
                StopAndReset();
                return;
            }

            // Switching tracks: synchronously tear down the current one. StopAndReset
            // unsubscribes PlaybackStopped before stopping, so the async stop callback
            // can't fire later and clobber the new playback's UI state.
            StopAndReset();

            if (string.IsNullOrEmpty(vm.SourcePath))
                return;

            string wavPath = null;
            try
            {
                wavPath = _decoder?.EnsureWav(vm.SourcePath);
            }
            catch (Exception)
            {
                // Decoder logs internally; just fail silently in the UI.
            }

            if (string.IsNullOrEmpty(wavPath))
            {
                vm.PlayButtonText = "âœ•";
                return;
            }

            try
            {
                _reader = new AudioFileReader(wavPath);
                _volumeProvider = new VolumeSampleProvider(_reader) { Volume = vm.EffectiveBankVolume };
                _player = new WaveOutEvent();
                _player.PlaybackStopped += OnPlaybackStopped;
                _player.Init(_volumeProvider);
                _player.Play();
                _playingRow = vm;
                vm.PlayButtonText = "â¸";
            }
            catch (Exception)
            {
                DisposePlayback();
                _playingRow = null;
                vm.PlayButtonText = "âœ•";
            }
        }

        // Natural end-of-track. Only fires here â€” manual stops via StopAndReset()
        // unsubscribe first so this can't run when we're switching tracks.
        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_playingRow != null)
                    _playingRow.PlayButtonText = "â–¶";
                DisposePlayback();
                _playingRow = null;
            });
        }

        // Synchronous teardown for both manual pause and track-switch.
        private void StopAndReset()
        {
            if (_player != null)
            {
                try
                {
                    _player.PlaybackStopped -= OnPlaybackStopped;
                    _player.Stop();
                }
                catch { /* fall through to dispose */ }
            }
            if (_playingRow != null)
                _playingRow.PlayButtonText = "â–¶";
            DisposePlayback();
            _playingRow = null;
        }

        private void DisposePlayback()
        {
            try { _player?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            _player = null;
            _reader = null;
            _volumeProvider = null;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            StopAndReset();
            foreach (var row in _rows)
                row.PropertyChanged -= OnRowPropertyChanged;
            _decoder?.Cleanup();
        }

        private void OnSave(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Result = _rows.ToList();
            Close();
        }

        private void OnCancel(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Result = null;
            Close();
        }
    }

    public class VolumeRowViewModel : INotifyPropertyChanged
    {
        public int OriginalIndex { get; set; }
        public string Title { get; set; }
        public string Filename { get; set; }
        public string SourcePath { get; set; }
        public float TargetLufs { get; set; }
        public float MaxMultiplier { get; set; }

        public bool HasMeasurement { get; set; }
        public float MeasuredLufs { get; set; }
        public float AutoGain { get; set; } = 1.0f;
        public bool WasClamped { get; set; }

        private float _userOverride = 1.0f;
        public float UserOverride
        {
            get => _userOverride;
            set
            {
                if (Math.Abs(_userOverride - value) < 0.0001f) return;
                _userOverride = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UserOverrideDecimal));
                OnPropertyChanged(nameof(EffectiveBankVolume));
                OnPropertyChanged(nameof(EffectiveDisplay));
            }
        }

        // Avalonia's NumericUpDown binds to decimal? â€” bridge to/from float here.
        public decimal? UserOverrideDecimal
        {
            get => (decimal)_userOverride;
            set => UserOverride = value.HasValue ? (float)value.Value : 1.0f;
        }

        public float EffectiveBankVolume => AutoGain * UserOverride;

        public string MeasuredDisplay => HasMeasurement
            ? MeasuredLufs.ToString("0.0", CultureInfo.InvariantCulture) + " LUFS"
            : "â€”";

        public string AutoGainDisplay => HasMeasurement
            ? AutoGain.ToString("0.00", CultureInfo.InvariantCulture) + "×"
            : "â€”";

        public string EffectiveDisplay => EffectiveBankVolume.ToString("0.00", CultureInfo.InvariantCulture) + "×";

        private string _playButtonText = "â–¶";
        public string PlayButtonText
        {
            get => _playButtonText;
            set
            {
                if (_playButtonText == value) return;
                _playButtonText = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
