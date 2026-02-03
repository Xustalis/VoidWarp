using System.IO;

namespace VoidWarp.Windows.ViewModels
{
    /// <summary>
    /// Represents a file pending to be sent in the queue.
    /// </summary>
    public class PendingFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public long FileSize { get; set; }
        
        public string FormattedSize
        {
            get
            {
                if (FileSize >= 1024 * 1024 * 1024)
                    return $"{FileSize / 1024.0 / 1024.0 / 1024.0:F2} GB";
                if (FileSize >= 1024 * 1024)
                    return $"{FileSize / 1024.0 / 1024.0:F1} MB";
                if (FileSize >= 1024)
                    return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize} 字节";
            }
        }
    }
}
