﻿// Copyright © 2017 Paddy Xu
// 
// This file is part of QuickLook program.
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MediaInfo;
using QuickLook.Common.Annotations;
using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using WPFMediaKit.DirectShow.Controls;
using WPFMediaKit.DirectShow.MediaPlayers;

namespace QuickLook.Plugin.VideoViewer
{
    /// <summary>
    ///     Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class ViewerPanel : UserControl, IDisposable, INotifyPropertyChanged
    {
        private readonly ContextObject _context;
        private BitmapSource _coverArt;
        
        private bool _hasVideo;
        private bool _isPlaying;
        private bool _wasPlaying;

        public ViewerPanel(ContextObject context)
        {
            InitializeComponent();

            // apply global theme
            Resources.MergedDictionaries[0].MergedDictionaries.Clear();

            _context = context;

            mediaElement.MediaUriPlayer.LAVFilterDirectory =
                IntPtr.Size == 8 ? "LAVFilters-0.72-x64\\" : "LAVFilters-0.72-x86\\";

            //ShowViedoControlContainer(null, null);
            viewerPanel.PreviewMouseMove += ShowViedoControlContainer;

            mediaElement.MediaUriPlayer.PlayerStateChanged += PlayerStateChanged;
            mediaElement.MediaOpened += MediaOpened;
            mediaElement.MediaEnded += MediaEnded;
            mediaElement.MediaFailed += MediaFailed;

            buttonPlayPause.Click += TogglePlayPause;
            buttonTime.Click += (sender, e) => buttonTime.Tag = (string) buttonTime.Tag == "Time" ? "Length" : "Time";
            buttonMute.Click += (sender, e) => volumeSliderLayer.Visibility = Visibility.Visible;
            volumeSliderLayer.MouseDown += (sender, e) => volumeSliderLayer.Visibility = Visibility.Collapsed;

            sliderProgress.PreviewMouseDown += (sender, e) =>
            {
                _wasPlaying = mediaElement.IsPlaying;
                mediaElement.Pause();
            };
            sliderProgress.PreviewMouseUp += (sender, e) =>
            {
                if (_wasPlaying) mediaElement.Play();
            };

            PreviewMouseWheel += (sender, e) => ChangeVolume((double) e.Delta / 120 * 0.04);
        }

        public bool HasVideo
        {
            get => _hasVideo;
            private set
            {
                if (value == _hasVideo) return;
                _hasVideo = value;
                OnPropertyChanged();
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (value == _isPlaying) return;
                _isPlaying = value;
                OnPropertyChanged();
            }
        }

        public BitmapSource CoverArt
        {
            get => _coverArt;
            private set
            {
                if (ReferenceEquals(value, _coverArt)) return;
                if (value == null) return;
                _coverArt = value;
                OnPropertyChanged();
            }
        }

        public void Dispose()
        {
            // old plugin use an int-typed "Volume" config key ranged from 0 to 100. Let's use a new one here.
            SettingHelper.Set("VolumeDouble", mediaElement.Volume);

            try
            {
                mediaElement?.Close();

                Task.Run(() =>
                {
                    mediaElement?.MediaUriPlayer.Dispose();
                    mediaElement = null;
                });
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void MediaOpened(object o, RoutedEventArgs args)
        {
            if (mediaElement == null)
                return;

            HasVideo = mediaElement.HasVideo;

            _context.IsBusy = false;
        }

        private void MediaFailed(object sender, MediaFailedEventArgs e)
        {
            ((MediaUriElement) sender).Dispatcher.BeginInvoke(new Action(() =>
            {
                _context.ViewerContent =
                    new Label {Content = e.Exception, VerticalAlignment = VerticalAlignment.Center};
                _context.IsBusy = false;
            }));
        }

        private void MediaEnded(object sender, RoutedEventArgs e)
        {
            if (mediaElement == null)
                return;

            IsPlaying = false;

            mediaElement.MediaPosition = 0;
            mediaElement.Pause();
        }

        private void ShowViedoControlContainer(object sender, MouseEventArgs e)
        {
            var show = (Storyboard) videoControlContainer.FindResource("ShowControlStoryboard");
            if (videoControlContainer.Opacity == 0 || videoControlContainer.Opacity == 1)
                show.Begin();
        }

        private void AutoHideViedoControlContainer(object sender, EventArgs e)
        {
            if (!HasVideo)
                return;

            if (videoControlContainer.IsMouseOver)
                return;

            var hide = (Storyboard) videoControlContainer.FindResource("HideControlStoryboard");

            hide.Begin();
        }

        private void PlayerStateChanged(PlayerState oldState, PlayerState newState)
        {
            switch (newState)
            {
                case PlayerState.Playing:
                    IsPlaying = true;
                    break;
                case PlayerState.Paused:
                case PlayerState.Stopped:
                case PlayerState.Closed:
                    IsPlaying = false;
                    break;
            }
        }

        private void UpdateMeta(string path, MediaInfo.MediaInfo info)
        {
            if (HasVideo)
                return;

            try
            {
                if (info == null)
                    throw new NullReferenceException();

                var title = info.Get(StreamKind.General, 0, "Title");
                var artist = info.Get(StreamKind.General, 0, "Performer");
                var album = info.Get(StreamKind.General, 0, "Album");

                metaTitle.Text = !string.IsNullOrWhiteSpace(title) ? title : Path.GetFileName(path);
                metaArtists.Text = artist;
                metaAlbum.Text = album;

                var cs = info.Get(StreamKind.General, 0, "Cover_Data");
                if (!string.IsNullOrEmpty(cs))
                    using (var ms = new MemoryStream(Convert.FromBase64String(cs)))
                    {
                        CoverArt = BitmapFrame.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    }
            }
            catch (Exception)
            {
                metaTitle.Text = Path.GetFileName(path);
                metaArtists.Text = metaAlbum.Text = string.Empty;
            }

            metaArtists.Visibility = string.IsNullOrEmpty(metaArtists.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
            metaAlbum.Visibility = string.IsNullOrEmpty(metaAlbum.Text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void ChangeVolume(double delta)
        {
            var newVol = mediaElement.Volume + delta;
            newVol = Math.Max(newVol, 0);
            newVol = Math.Min(newVol, 1);

            mediaElement.Volume = newVol;
        }

        private void TogglePlayPause(object sender, EventArgs e)
        {
            if (mediaElement.IsPlaying)
                mediaElement.Pause();
            else
                mediaElement.Play();
        }

        public void LoadAndPlay(string path, MediaInfo.MediaInfo info)
        {
            UpdateMeta(path, info);
            
            // detect rotation
            double.TryParse(info?.Get(StreamKind.Video, 0, "Rotation"), out var rotation);
            if (Math.Abs(rotation) > 0.1)
                mediaElement.LayoutTransform = new RotateTransform(rotation, 0.5, 0.5);

            mediaElement.Source = new Uri(path);
            // old plugin use an int-typed "Volume" config key ranged from 0 to 100. Let's use a new one here.
            mediaElement.Volume = SettingHelper.Get("VolumeDouble", 1d);

            mediaElement.Play();
        }

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
