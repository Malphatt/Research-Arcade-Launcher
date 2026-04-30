using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XamlAnimatedGif;

namespace ArcademiaGameLauncher.UserControls
{
    public sealed class ThumbnailEntry
    {
        public readonly ImageSource StaticSource;
        public readonly string TempFilePath;
        public readonly bool IsGif;

        public ThumbnailEntry(ImageSource staticImage) => StaticSource = staticImage;

        public ThumbnailEntry(ImageSource firstFrame, string tempFilePath)
        {
            StaticSource = firstFrame;
            TempFilePath = tempFilePath;
            IsGif = true;
        }
    }

    public partial class HomeThumbnailScroll : UserControl
    {
        private readonly List<Image> _gifImages = new();
        private const double DisplayWidth = 1320;
        private const double DisplayHeight = 1080;

        // 7:3 thumbnail ratio
        private const double ThumbW = 350;
        private const double ThumbH = 150;

        private const double GapX = 15;
        private const double GapY = 15;
        private const double CellW = ThumbW + GapX;
        private const double CellH = ThumbH + GapY;

        private const int NumRows = 14;
        private const int MinPassItems = 10;
        private const double ScrollSeconds = 60.0;

        public HomeThumbnailScroll()
        {
            InitializeComponent();
        }

        public void StopAndClear()
        {
            ScrollTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            foreach (var img in _gifImages)
                AnimationBehavior.SetSourceUri(img, null);
            _gifImages.Clear();
            GifMasterContainer.Children.Clear();
            RowsContainer.Children.Clear();
        }

        public void LoadThumbnails(IReadOnlyList<ThumbnailEntry> images)
        {
            StopAndClear();

            if (images == null || images.Count == 0)
                return;

            var pass = new List<ThumbnailEntry>(images);
            int origCount = pass.Count;
            int idx = 0;
            while (pass.Count < MinPassItems)
                pass.Add(pass[idx++ % origCount]);

            int n = pass.Count;
            double passW = n * CellW;
            double totalW = 2 * passW;
            double totalH = NumRows * CellH;

            var gifMasters = new Dictionary<ThumbnailEntry, Image>();
            foreach (var entry in pass)
            {
                if (!entry.IsGif || gifMasters.ContainsKey(entry))
                    continue;

                var master = new Image
                {
                    Width = ThumbW,
                    Height = ThumbH,
                    Stretch = Stretch.UniformToFill,
                    Source = entry.StaticSource,
                };

                AnimationBehavior.SetSourceUri(
                    master,
                    new Uri(entry.TempFilePath, UriKind.Absolute)
                );
                AnimationBehavior.SetCacheFramesInMemory(master, false);
                Canvas.SetLeft(master, 0);
                Canvas.SetTop(master, 0);
                GifMasterContainer.Children.Add(master);
                _gifImages.Add(master);
                gifMasters[entry] = master;
            }

            int[] offsets = BuildShuffledOffsets(NumRows, n);

            for (int row = 0; row < NumRows; row++)
            {
                var rowPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, GapY),
                };

                int offset = offsets[row];
                for (int repeat = 0; repeat < 2; repeat++)
                for (int i = 0; i < n; i++)
                    rowPanel.Children.Add(MakeThumbnail(pass[(i + offset) % n], gifMasters));

                RowsContainer.Children.Add(rowPanel);
            }

            Canvas.SetLeft(RowsContainer, (DisplayWidth - totalW) / 2.0);
            Canvas.SetTop(RowsContainer, (DisplayHeight - totalH) / 2.0);

            var anim = new DoubleAnimation
            {
                From = 0,
                To = -passW,
                Duration = new Duration(TimeSpan.FromSeconds(ScrollSeconds)),
                RepeatBehavior = RepeatBehavior.Forever,
            };
            ScrollTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private static int[] BuildShuffledOffsets(int numRows, int n)
        {
            var offsets = new int[numRows];

            for (int i = 0; i < numRows; i++)
                offsets[i] = i % n;

            var rng = new Random(42);
            for (int i = numRows - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int tmp = offsets[i];
                offsets[i] = offsets[j];
                offsets[j] = tmp;
            }

            return offsets;
        }

        private static Border MakeThumbnail(
            ThumbnailEntry entry,
            IReadOnlyDictionary<ThumbnailEntry, Image> gifMasters
        )
        {
            var brush = new ImageBrush
            {
                ImageSource = entry.StaticSource,
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center,
            };

            if (entry.IsGif && gifMasters.TryGetValue(entry, out var master))
            {
                BindingOperations.SetBinding(
                    brush,
                    ImageBrush.ImageSourceProperty,
                    new Binding(nameof(Image.Source)) { Source = master }
                );
            }

            return new Border
            {
                Width = ThumbW,
                Height = ThumbH,
                Margin = new Thickness(0, 0, GapX, 0),
                CornerRadius = new CornerRadius(8),
                Background = brush,
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(2),
            };
        }
    }
}
