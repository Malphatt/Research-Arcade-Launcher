using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ArcademiaGameLauncher.Utilis
{
    public static class TextBlockExtensions
    {
        // A simple cache‐key type so we can skip re‐measuring if nothing relevant changed.
        private class FitKey : IEquatable<FitKey>
        {
            public string Text { get; }
            public double Width { get; }
            public double Height { get; }
            public FontFamily Family { get; }
            public FontStyle Style { get; }
            public FontWeight Weight { get; }
            public FontStretch Stretch { get; }
            public int MaxLines { get; }
            public double TargetSize { get; }
            public double MinSize { get; }
            public double Precision { get; }

            public FitKey(
                string text,
                double width,
                double height,
                FontFamily family,
                FontStyle style,
                FontWeight weight,
                FontStretch stretch,
                int maxLines,
                double targetSize,
                double minSize,
                double precision
            )
            {
                Text = text;
                Width = width;
                Height = height;
                Family = family;
                Style = style;
                Weight = weight;
                Stretch = stretch;
                MaxLines = maxLines;
                TargetSize = targetSize;
                MinSize = minSize;
                Precision = precision;
            }

            public override bool Equals(object obj) => Equals(obj as FitKey);

            public bool Equals(FitKey other)
            {
                if (other == null)
                    return false;
                return Text == other.Text
                    && Width == other.Width
                    && Height == other.Height
                    && Family == other.Family
                    && Style == other.Style
                    && Weight == other.Weight
                    && Stretch == other.Stretch
                    && MaxLines == other.MaxLines
                    && TargetSize == other.TargetSize
                    && MinSize == other.MinSize
                    && Precision == other.Precision;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Text?.GetHashCode() ?? 0;
                    hash = hash * 397 ^ Width.GetHashCode();
                    hash = hash * 397 ^ Height.GetHashCode();
                    hash = hash * 397 ^ (Family?.GetHashCode() ?? 0);
                    hash = hash * 397 ^ Style.GetHashCode();
                    hash = hash * 397 ^ Weight.GetHashCode();
                    hash = hash * 397 ^ Stretch.GetHashCode();
                    hash = hash * 397 ^ MaxLines;
                    hash = hash * 397 ^ TargetSize.GetHashCode();
                    hash = hash * 397 ^ MinSize.GetHashCode();
                    hash = hash * 397 ^ Precision.GetHashCode();
                    return hash;
                }
            }
        }

        // Cache the last parameters + result to skip re‐measuring if identical.
        private static FitKey _lastKey;
        private static double _lastFittedSize;

        /// <summary>
        /// Adjusts the FontSize of <paramref name="textBlock"/> so that
        /// <paramref name="desiredText"/> fits within its ActualWidth/ActualHeight,
        /// wrapping into at most <paramref name="maxLines"/> lines.
        /// </summary>
        public static void FitTextToTextBlock(
            this TextBlock textBlock,
            string desiredText,
            double targetFontSize,
            int maxLines = 100,
            double minFontSize = 8.0,
            double precision = 0.1
        )
        {
            if (textBlock == null || string.IsNullOrWhiteSpace(desiredText))
                return;

            // If we call this too early (before WPF measures/arranges), ActualWidth/Height may be zero.
            // Defer once layout is ready:
            if (textBlock.ActualWidth <= 0 || textBlock.ActualHeight <= 0)
            {
                void Handler(object s, SizeChangedEventArgs e)
                {
                    // Only run once:
                    if (textBlock.ActualWidth > 0 && textBlock.ActualHeight > 0)
                    {
                        textBlock.SizeChanged -= Handler;
                        textBlock.FitTextToTextBlock(
                            desiredText,
                            targetFontSize,
                            maxLines,
                            minFontSize,
                            precision
                        );
                    }
                }
                textBlock.SizeChanged += Handler;
                return;
            }

            // Build a cache key for all inputs that affect the measured font size.
            var key = new FitKey(
                desiredText,
                textBlock.ActualWidth,
                textBlock.ActualHeight,
                textBlock.FontFamily,
                textBlock.FontStyle,
                textBlock.FontWeight,
                textBlock.FontStretch,
                maxLines,
                targetFontSize,
                minFontSize,
                precision
            );

            // If nothing changed, just re‐apply the cached size:
            if (_lastKey != null && _lastKey.Equals(key))
            {
                textBlock.Text = desiredText;
                textBlock.TextWrapping = TextWrapping.Wrap;
                textBlock.FontSize = Math.Max(_lastFittedSize, minFontSize);
                return;
            }

            // Prepare for measurement:
            double availableWidth = textBlock.ActualWidth;
            double availableHeight = textBlock.ActualHeight;

            // We need a Typeface + a GlyphTypeface so we can compute per‐line spacing:
            Typeface typeface = new Typeface(
                textBlock.FontFamily,
                textBlock.FontStyle,
                textBlock.FontWeight,
                textBlock.FontStretch
            );

            if (!typeface.TryGetGlyphTypeface(out GlyphTypeface glyph))
            {
                // If the Typeface doesn’t yield a GlyphTypeface (rare),
                // fall back to assuming lineHeight == fontSize:
                // (≤1%‐ish error, but safe.)
                glyph = null;
            }

            // Utility: compute line‐height for a given fontSize:
            double GetLineHeight(double fontSize)
            {
                // GlyphTypeface.Height is "cell height relative to em size" – multiply by fontSize → device‐units/px.
                if (glyph != null)
                    return glyph.Height * fontSize; // :contentReference[oaicite:0]{index=0}
                else
                    return fontSize; // fallback
            }

            // Utility: measure how many lines and total height this text uses at a given fontSize:
            bool TextFits(double fontSize)
            {
                // Create a FormattedText *without* MaxLineCount so we can see the real Height:
                double dpiFactor = VisualTreeHelper.GetDpi(textBlock).PixelsPerDip;
                var ft = new FormattedText(
                    desiredText,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    Brushes.Black,
                    dpiFactor
                )
                {
                    MaxTextWidth = availableWidth,
                    Trimming = TextTrimming.None,
                    TextAlignment = textBlock.TextAlignment,
                };

                double totalHeight = ft.Height;
                double lineH = GetLineHeight(fontSize);

                // Calculate linesUsed = Ceiling(totalHeight / lineH).
                // If there's any rounding‐error (e.g. exact multiples), Ceiling will yield the correct integer.
                int linesUsed = (int)Math.Ceiling(totalHeight / lineH);

                bool fitsLines = linesUsed <= maxLines;
                bool fitsHeight = totalHeight <= availableHeight;

                return fitsLines && fitsHeight;
            }

            // 1) If targetFontSize already fits, no need to search:
            double bestSize = minFontSize;
            if (TextFits(targetFontSize))
            {
                bestSize = targetFontSize;
            }
            else
            {
                // 2) Otherwise, binary‐search between [minFontSize, targetFontSize], until range ≤ precision:
                double low = minFontSize;
                double high = targetFontSize;

                while (high - low > precision)
                {
                    double mid = (low + high) / 2.0;
                    if (TextFits(mid))
                    {
                        bestSize = mid;
                        low = mid; // try a larger size next
                    }
                    else
                    {
                        high = mid; // mid didn’t fit, try smaller
                    }
                }
            }

            bestSize = Math.Max(bestSize, minFontSize);

            // Cache the result for next time:
            _lastKey = key;
            _lastFittedSize = bestSize;

            // Finally, apply to the TextBlock:
            textBlock.Text = desiredText;
            textBlock.TextWrapping = TextWrapping.Wrap;
            textBlock.FontSize = bestSize;
            textBlock.LineHeight = GetLineHeight(bestSize * 1.2); // Add some extra space for readability
        }
    }
}
