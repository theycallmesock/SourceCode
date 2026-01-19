using System;

namespace WinOptPro.Models
{
    public class ScriptItem
    {
        public string Category { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public bool IsSelected { get; set; } = false;
        public bool HasUndo { get; set; } = false;
    }
}