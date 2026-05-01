using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Suterusu.Services
{
    public static class ImageResizer
    {
        public static byte[] Downscale(byte[] imageData, int maxDimension)
        {
            if (imageData == null || imageData.Length == 0)
                return imageData;

            using (var ms = new MemoryStream(imageData))
            using (var image = Image.FromStream(ms))
            {
                int width = image.Width;
                int height = image.Height;

                if (width <= maxDimension && height <= maxDimension)
                    return imageData;

                double scale = Math.Min((double)maxDimension / width, (double)maxDimension / height);
                int newWidth = (int)(width * scale);
                int newHeight = (int)(height * scale);

                using (var bitmap = new Bitmap(newWidth, newHeight, PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(image, 0, 0, newWidth, newHeight);

                    using (var outMs = new MemoryStream())
                    {
                        bitmap.Save(outMs, ImageFormat.Png);
                        return outMs.ToArray();
                    }
                }
            }
        }
    }
}
