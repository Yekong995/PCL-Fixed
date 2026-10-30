// 由 VB Modules\Base\MyBitmap.vb 迁移而来：一个万能的自动图片类型转换工具类。
#nullable disable

using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PCLCS.Modules;

public class MyBitmap {

    /// <summary>
    /// 存储的图片
    /// </summary>
    public Bitmap Pic;

    // 自动类型转换
    // 支持的类：Image，ImageSource，Bitmap，ImageBrush，BitmapSource
    public static implicit operator MyBitmap(Image Image) {
        if (Image is null) return null;
        return new MyBitmap(Image);
    }
    public static implicit operator Image(MyBitmap Image) {
        if (Image is null) return null;
        return Image.Pic;
    }
    public static implicit operator MyBitmap(ImageSource Image) {
        if (Image is null) return null;
        return new MyBitmap(Image);
    }
    public static implicit operator ImageSource(MyBitmap Image) {
        if (Image is null) return null;
        Bitmap BitmapPic = Image.Pic;
        Rectangle rect = new Rectangle(0, 0, BitmapPic.Width, BitmapPic.Height);
        BitmapData bitmapData = BitmapPic.LockBits(rect, ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try {
            BitmapSource Result = BitmapSource.Create(BitmapPic.Width, BitmapPic.Height, BitmapPic.HorizontalResolution, BitmapPic.VerticalResolution,
                                       PixelFormats.Bgra32, null, bitmapData.Scan0,
                                       rect.Width * rect.Height * 4, bitmapData.Stride);
            Result.Freeze();
            return Result;
        } finally {
            BitmapPic.UnlockBits(bitmapData);
        }
    }
    public static implicit operator MyBitmap(Bitmap Image) {
        if (Image is null) return null;
        return new MyBitmap(Image);
    }
    public static implicit operator Bitmap(MyBitmap Image) {
        if (Image is null) return null;
        return Image.Pic;
    }
    public static implicit operator MyBitmap(ImageBrush Image) {
        if (Image is null) return null;
        return new MyBitmap(Image);
    }
    public static implicit operator ImageBrush(MyBitmap Image) {
        if (Image is null) return null;
        return new ImageBrush(new MyBitmap(Image.Pic));
    }

    // 构造函数
    public MyBitmap() {
    }
    public MyBitmap(string FilePathOrResourceName) {
        try {
            FilePathOrResourceName = FilePathOrResourceName.Replace("pack://application:,,,/images/", PclCore.PathImage);
            if (FilePathOrResourceName.StartsWithF(PclCore.PathImage)) {
                // 使用缓存
                if (Cache.ContainsKey(FilePathOrResourceName)) {
                    Pic = Cache[FilePathOrResourceName].Pic;
                } else {
                    Pic = new MyBitmap((ImageSource)new ImageSourceConverter().ConvertFromString(FilePathOrResourceName));
                    Cache.TryAdd(FilePathOrResourceName, Pic);
                }
            } else {
                // 使用这种自己接管 FileStream 的方法加载才能解除文件占用
                using (Stream InputStream = FileUtils.ReadAsStream(FilePathOrResourceName)) {
                    // 判断是否为 WebP 文件头
                    byte[] Header = new byte[2];
                    InputStream.Read(Header, 0, 2);
                    InputStream.Seek(0, SeekOrigin.Begin);
                    if (Header[0] == 82 && Header[1] == 73) {
                        // 读取 WebP
                        byte[] FileBytes = new byte[InputStream.Length];
                        InputStream.Read(FileBytes, 0, FileBytes.Length);
                        Pic = WebPDecoder.DecodeFromBytes(FileBytes); // 将代码隔离在另外一个类中，这样只要不走进这个分支就不会加载 Imazen.WebP.dll
                    } else {
                        Pic = new Bitmap(InputStream);
                    }
                }
            }
        } catch (Exception ex) {
            Pic = (Bitmap)Application.Current.TryFindResource(FilePathOrResourceName);
            if (Pic is null) {
                Pic = new Bitmap(1, 1);
                if (ex is ArgumentException) {
                    throw new Exception($"图片格式不支持，或图片文件损坏（{FilePathOrResourceName}）", ex);
                } else {
                    throw new Exception($"加载 MyBitmap 意外失败（{FilePathOrResourceName}）", ex);
                }
            } else {
                Logger.Warn(ex, $"指定类型有误的 MyBitmap 加载（{FilePathOrResourceName}）");
                return;
            }
        }
    }
    public MyBitmap(ImageSource Image) {
        using (MemoryStream MS = new MemoryStream()) {
            PngBitmapEncoder Encoder = new PngBitmapEncoder();
            Encoder.Frames.Add(BitmapFrame.Create((BitmapSource)Image));
            Encoder.Save(MS);
            Pic = new Bitmap(MS);
        }
    }
    public MyBitmap(Image Image) {
        Pic = (Bitmap)Image;
    }
    public MyBitmap(Bitmap Image) {
        Pic = Image;
    }
    public MyBitmap(ImageBrush Image) {
        using (MemoryStream MS = new MemoryStream()) {
            BmpBitmapEncoder Encoder = new BmpBitmapEncoder();
            Encoder.Frames.Add(BitmapFrame.Create((BitmapSource)Image.ImageSource));
            Encoder.Save(MS);
            Pic = new Bitmap(MS);
        }
    }

    /// <summary>
    /// pack:// 图片路径缓存。
    /// </summary>
    private static readonly ConcurrentDictionary<string, MyBitmap> Cache = new ConcurrentDictionary<string, MyBitmap>();

    private static class WebPDecoder { // 将代码隔离在另外一个类中，这样只要不调用这个方法就不会加载 Imazen.WebP.dll
        public static Bitmap DecodeFromBytes(byte[] Bytes) {
            return new Imazen.WebP.SimpleDecoder().DecodeFromBytes(Bytes, Bytes.Length);
        }
    }

    /// <summary>
    /// 获取裁切的图片，这个方法不会导致原对象改变且会返回一个新的对象。
    /// </summary>
    public MyBitmap Clip(int X, int Y, int Width, int Height) {
        Bitmap bmp = new Bitmap(Width, Height, Pic.PixelFormat);
        bmp.SetResolution(Pic.HorizontalResolution, Pic.VerticalResolution);
        using (Graphics g = Graphics.FromImage(bmp)) {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.TranslateTransform(-X, -Y);
            g.DrawImage(Pic, new Rectangle(0, 0, Pic.Width, Pic.Height));
        }
        return bmp;
    }

    /// <summary>
    /// 获取旋转或翻转后的图片，这个方法不会导致原对象改变且会返回一个新的对象。
    /// </summary>
    public MyBitmap RotateFlip(RotateFlipType Type) {
        Bitmap bmp = new Bitmap(Pic);
        bmp.SetResolution(Pic.HorizontalResolution, Pic.VerticalResolution);
        bmp.RotateFlip(Type);
        return bmp;
    }

    /// <summary>
    /// 将图像保存到文件。
    /// </summary>
    public void Save(string FilePath) {
        BitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create((BitmapSource)(ImageSource)this));
        using (Stream fileStream = FileUtils.CreateAsStream(FilePath)) {
            encoder.Save(fileStream);
        }
    }

}
