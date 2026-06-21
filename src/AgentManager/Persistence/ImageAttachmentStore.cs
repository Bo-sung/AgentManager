using System.Windows.Media.Imaging;

namespace AgentManager.Persistence;

public static class ImageAttachmentStore
{
    public static string Dir { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AgentManager", "attachments");

    /// <summary>Encode a clipboard/source image to a timestamped PNG under Dir; return the path (or null on failure).</summary>
    public static string? SavePng(BitmapSource image)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Dir);
            var file = System.IO.Path.Combine(Dir,
                "paste-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(image));
            using (var fs = System.IO.File.Create(file)) enc.Save(fs);
            return file;
        }
        catch { return null; }
    }
}
