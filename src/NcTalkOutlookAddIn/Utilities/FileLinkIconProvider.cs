// Copyright (c) 2025 Bastian Kleinschmidt
// Licensed under the GNU Affero General Public License v3.0.
// See LICENSE.txt for details.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace NcTalkOutlookAddIn.Utilities
{
    internal static class FileLinkIconProvider
    {
        internal const string LocalSourceKey = "source-local";
        internal const string NextcloudSourceKey = "source-nextcloud";
        internal const string FolderKey = "folder";
        internal const string GenericFileKey = "file";

        private static readonly IDictionary<string, Color> ExtensionColors =
            new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
            {
                { ".pdf", Color.FromArgb(229, 57, 91) },
                { ".doc", Color.FromArgb(55, 116, 214) },
                { ".docx", Color.FromArgb(55, 116, 214) },
                { ".odt", Color.FromArgb(55, 116, 214) },
                { ".xls", Color.FromArgb(79, 177, 96) },
                { ".xlsx", Color.FromArgb(79, 177, 96) },
                { ".ods", Color.FromArgb(79, 177, 96) },
                { ".csv", Color.FromArgb(79, 177, 96) },
                { ".ppt", Color.FromArgb(238, 127, 54) },
                { ".pptx", Color.FromArgb(238, 127, 54) },
                { ".odp", Color.FromArgb(238, 127, 54) },
                { ".jpg", Color.FromArgb(152, 96, 201) },
                { ".jpeg", Color.FromArgb(152, 96, 201) },
                { ".png", Color.FromArgb(152, 96, 201) },
                { ".gif", Color.FromArgb(152, 96, 201) },
                { ".webp", Color.FromArgb(152, 96, 201) },
                { ".svg", Color.FromArgb(152, 96, 201) },
                { ".zip", Color.FromArgb(191, 151, 67) },
                { ".7z", Color.FromArgb(191, 151, 67) },
                { ".rar", Color.FromArgb(191, 151, 67) },
                { ".tar", Color.FromArgb(191, 151, 67) },
                { ".gz", Color.FromArgb(191, 151, 67) },
                { ".mp3", Color.FromArgb(0, 166, 166) },
                { ".wav", Color.FromArgb(0, 166, 166) },
                { ".ogg", Color.FromArgb(0, 166, 166) },
                { ".mp4", Color.FromArgb(43, 151, 201) },
                { ".mkv", Color.FromArgb(43, 151, 201) },
                { ".mov", Color.FromArgb(43, 151, 201) },
                { ".avi", Color.FromArgb(43, 151, 201) },
                { ".txt", Color.FromArgb(170, 170, 170) },
                { ".md", Color.FromArgb(170, 170, 170) },
                { ".html", Color.FromArgb(235, 116, 46) },
                { ".htm", Color.FromArgb(235, 116, 46) },
                { ".json", Color.FromArgb(224, 190, 51) },
                { ".xml", Color.FromArgb(224, 190, 51) }
            };

        internal static ImageList CreateImageList(
            int width,
            int height)
        {
            int safeWidth = Math.Max(16, width);
            int safeHeight = Math.Max(20, height);
            var images = new ImageList
            {
                ColorDepth = ColorDepth.Depth32Bit,
                ImageSize = new Size(safeWidth, safeHeight),
                TransparentColor = Color.Transparent
            };
            images.Images.Add(
                LocalSourceKey,
                DrawLocalSource(safeWidth, safeHeight));
            images.Images.Add(
                NextcloudSourceKey,
                DrawNextcloudSource(safeWidth, safeHeight));
            images.Images.Add(
                FolderKey,
                DrawFolder(safeWidth, safeHeight));
            images.Images.Add(
                GenericFileKey,
                DrawFile(
                    safeWidth,
                    safeHeight,
                    Color.FromArgb(196, 188, 211)));
            foreach (KeyValuePair<string, Color> pair in ExtensionColors)
            {
                images.Images.Add(
                    BuildExtensionKey(pair.Key),
                    DrawFile(safeWidth, safeHeight, pair.Value));
            }
            return images;
        }

        internal static string GetFileKey(string fileName)
        {
            string extension = Path.GetExtension(fileName ?? string.Empty);
            return !string.IsNullOrWhiteSpace(extension)
                   && ExtensionColors.ContainsKey(extension)
                ? BuildExtensionKey(extension)
                : GenericFileKey;
        }

        private static string BuildExtensionKey(string extension)
        {
            return "file-" + (extension ?? string.Empty)
                .TrimStart('.')
                .ToLowerInvariant();
        }

        private static Bitmap CreateCanvas(int width, int height)
        {
            return new Bitmap(width, height);
        }

        private static Bitmap DrawFolder(int width, int height)
        {
            Bitmap bitmap = CreateCanvas(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = CenteredBounds(width, height, 17, 14);
                using (var back = new SolidBrush(
                    Color.FromArgb(226, 163, 45)))
                using (var front = new SolidBrush(
                    Color.FromArgb(255, 190, 57)))
                {
                    graphics.FillRectangle(
                        back,
                        bounds.Left + 1,
                        bounds.Top,
                        7,
                        4);
                    graphics.FillRectangle(
                        front,
                        bounds.Left,
                        bounds.Top + 3,
                        bounds.Width,
                        bounds.Height - 3);
                }
            }
            return bitmap;
        }

        private static Bitmap DrawFile(
            int width,
            int height,
            Color color)
        {
            Bitmap bitmap = CreateCanvas(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = CenteredBounds(width, height, 13, 17);
                var outline = new[]
                {
                    new Point(bounds.Left, bounds.Top),
                    new Point(bounds.Right - 5, bounds.Top),
                    new Point(bounds.Right, bounds.Top + 5),
                    new Point(bounds.Right, bounds.Bottom),
                    new Point(bounds.Left, bounds.Bottom)
                };
                using (var fill = new SolidBrush(
                    Color.FromArgb(245, 245, 247)))
                using (var accent = new SolidBrush(color))
                using (var border = new Pen(
                    Color.FromArgb(145, 145, 150)))
                {
                    graphics.FillPolygon(fill, outline);
                    graphics.DrawPolygon(border, outline);
                    graphics.FillRectangle(
                        accent,
                        bounds.Left,
                        bounds.Bottom - 5,
                        bounds.Width,
                        5);
                    graphics.DrawLine(
                        border,
                        bounds.Right - 5,
                        bounds.Top,
                        bounds.Right - 5,
                        bounds.Top + 5);
                    graphics.DrawLine(
                        border,
                        bounds.Right - 5,
                        bounds.Top + 5,
                        bounds.Right,
                        bounds.Top + 5);
                }
            }
            return bitmap;
        }

        private static Bitmap DrawNextcloudSource(
            int width,
            int height)
        {
            Bitmap bitmap = CreateCanvas(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = CenteredBounds(width, height, 19, 13);
                using (var fill = new SolidBrush(
                    Color.FromArgb(0, 130, 201)))
                using (var white = new Pen(Color.White, 1.6f))
                {
                    graphics.FillEllipse(
                        fill,
                        bounds.Left,
                        bounds.Top,
                        9,
                        9);
                    graphics.FillEllipse(
                        fill,
                        bounds.Right - 9,
                        bounds.Top,
                        9,
                        9);
                    graphics.FillEllipse(
                        fill,
                        bounds.Left + 5,
                        bounds.Top + 3,
                        9,
                        9);
                    graphics.DrawEllipse(
                        white,
                        bounds.Left + 6,
                        bounds.Top + 4,
                        7,
                        7);
                }
            }
            return bitmap;
        }

        private static Bitmap DrawLocalSource(
            int width,
            int height)
        {
            Bitmap bitmap = CreateCanvas(width, height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle bounds = CenteredBounds(width, height, 17, 14);
                using (var pen = new Pen(
                    Color.FromArgb(0, 130, 201),
                    1.8f))
                {
                    graphics.DrawRectangle(
                        pen,
                        bounds.Left,
                        bounds.Top,
                        bounds.Width - 1,
                        bounds.Height - 4);
                    graphics.DrawLine(
                        pen,
                        bounds.Left + 5,
                        bounds.Bottom - 1,
                        bounds.Right - 5,
                        bounds.Bottom - 1);
                    graphics.DrawLine(
                        pen,
                        bounds.Left + 8,
                        bounds.Bottom - 4,
                        bounds.Left + 8,
                        bounds.Bottom - 1);
                }
            }
            return bitmap;
        }

        private static Rectangle CenteredBounds(
            int width,
            int height,
            int desiredWidth,
            int desiredHeight)
        {
            int actualWidth = Math.Min(width, desiredWidth);
            int actualHeight = Math.Min(height, desiredHeight);
            return new Rectangle(
                Math.Max(0, (width - actualWidth) / 2),
                Math.Max(0, (height - actualHeight) / 2),
                actualWidth,
                actualHeight);
        }
    }
}
