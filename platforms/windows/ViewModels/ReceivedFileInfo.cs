using System;

namespace VoidWarp.Windows.ViewModels
{
    public class ReceivedFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty; // Formatted size
        public string SenderName { get; set; } = string.Empty;
        public DateTime ReceivedTime { get; set; } = DateTime.Now;
        public bool FileExists => System.IO.File.Exists(FilePath);
    }
}
