using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace Suterusu.Services
{
    public class ScreenCaptureService
    {
        public byte[] CaptureRegion(Rectangle region)
        {
            if (region.Width <= 0 || region.Height <= 0)
                return null;

            using (var bitmap = new Bitmap(region.Width, region.Height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(region.X, region.Y, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
                }

                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return stream.ToArray();
                }
            }
        }
    }
}