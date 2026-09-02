using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace ZipDrop
{
    public static class ZipExtractor
    {
        static readonly Encoding Utf8Strict = Encoding.GetEncoding(65001,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        static readonly Encoding Gbk = Encoding.GetEncoding(936);

        /// <summary>直接解析 zip 中央目录，返回每个条目解码后的真实文件名（与条目顺序一致）。</summary>
        public static List<string> ReadZipNames(string path)
        {
            var names = new List<string>();
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs))
            {
                long len = fs.Length;

                // 从尾部倒找 EOCD（PK\x05\x06），用注释长度校验
                long eocd = -1;
                int tailLen = (int)Math.Min(len, 22 + 65535);
                fs.Seek(len - tailLen, SeekOrigin.Begin);
                byte[] tail = br.ReadBytes(tailLen);
                for (int i = tail.Length - 22; i >= 0; i--)
                {
                    if (tail[i] == 0x50 && tail[i + 1] == 0x4b && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
                    {
                        int commentLen = BitConverter.ToUInt16(tail, i + 20);
                        if (i + 22 + commentLen == tail.Length) { eocd = len - tailLen + i; break; }
                    }
                }
                if (eocd < 0) throw new InvalidDataException("无法定位 zip 目录(EOCD)，文件可能已损坏");

                fs.Seek(eocd + 10, SeekOrigin.Begin);
                long totalEntries = br.ReadUInt16();
                br.ReadUInt32();                                  // 中央目录大小(4字节)
                long cdOffset = br.ReadUInt32();                  // 中央目录偏移

                // Zip64 超大包
                if (totalEntries == 0xFFFF || cdOffset == 0xFFFFFFFF)
                {
                    fs.Seek(eocd - 20, SeekOrigin.Begin);
                    if (br.ReadUInt32() == 0x07064b50)
                    {
                        br.ReadUInt32();                          // 所在磁盘
                        long z64 = (long)br.ReadUInt64();         // zip64 EOCD 偏移
                        fs.Seek(z64 + 32, SeekOrigin.Begin);
                        totalEntries = (long)br.ReadUInt64();
                        fs.Seek(z64 + 40, SeekOrigin.Begin);
                        br.ReadUInt64();                          // 目录大小
                        cdOffset = (long)br.ReadUInt64();         // 目录偏移
                    }
                }

                fs.Seek(cdOffset, SeekOrigin.Begin);
                for (long i = 0; i < totalEntries; i++)
                {
                    if (br.ReadUInt32() != 0x02014b50) throw new InvalidDataException("zip 中央目录损坏");
                    br.ReadUInt16(); br.ReadUInt16();             // version made/needed
                    ushort flags = br.ReadUInt16();
                    br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt16();   // method, time, date
                    br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();   // crc, comp, uncomp
                    int nameLen = br.ReadUInt16();
                    int extraLen = br.ReadUInt16();
                    int commentLen = br.ReadUInt16();
                    br.ReadUInt16(); br.ReadUInt16();             // disk, internal attrs
                    br.ReadUInt32(); br.ReadUInt32();             // external attrs, local offset
                    byte[] nameBytes = br.ReadBytes(nameLen);
                    if (extraLen > 0) br.ReadBytes(extraLen);
                    if (commentLen > 0) br.ReadBytes(commentLen);

                    names.Add(DecodeName(nameBytes, (flags & 0x800) != 0));
                }
            }
            return names;
        }

        static string DecodeName(byte[] bytes, bool utf8Flag)
        {
            // 有 UTF-8 标志位 → UTF-8；无标志位 → 字节合法 UTF-8 则按 UTF-8（macOS/手机），否则按 GBK
            try { return Utf8Strict.GetString(bytes); }
            catch (DecoderFallbackException) { return Gbk.GetString(bytes); }
        }

        public class ExtractResult
        {
            public bool Ok;
            public string Message;
            public string Output;
        }

        public static ExtractResult Extract(string zipPath)
        {
            var result = new ExtractResult();
            string dest = "";
            int skipped = 0;
            try
            {
                if (!File.Exists(zipPath)) { result.Message = "找不到文件: " + zipPath; return result; }
                if (Path.GetExtension(zipPath).ToLowerInvariant() != ".zip")
                { result.Message = "不是 zip 文件，已跳过"; return result; }

                string src = Path.GetFullPath(zipPath);
                List<string> names = ReadZipNames(src);
                string dir = Path.GetDirectoryName(src);
                string baseName = Path.GetFileNameWithoutExtension(src);

                // 不冲突的输出文件夹（xxx_解压、xxx_解压_2 ...）
                dest = Path.Combine(dir, baseName + "_解压");
                int n = 2;
                while (Directory.Exists(dest)) { dest = Path.Combine(dir, baseName + "_解压_" + n); n++; }
                Directory.CreateDirectory(dest);

                using (var fs = File.OpenRead(src))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Read))
                {
                    for (int i = 0; i < archive.Entries.Count; i++)
                    {
                        var entry = archive.Entries[i];
                        string name = i < names.Count ? names[i] : entry.FullName;
                        if (name.Length == 0) continue;
                        name = name.TrimEnd('/');

                        // 跳过 macOS 垃圾（__MACOSX 目录、._ 开头文件）
                        string[] segs = name.Split('/');
                        if (segs[0] == "__MACOSX") continue;
                        if (segs.Any(s => s.StartsWith("._"))) continue;

                        // 防路径穿越/非法路径：跳过该条目
                        bool bad = name.Contains("..");
                        if (!bad) bad = name.IndexOfAny(new char[] { '<', '>', ':', '"', '|', '?', '*' }) >= 0;
                        if (!bad) bad = name.StartsWith("/") || name.StartsWith("\\") || name.StartsWith("~");
                        if (bad) { skipped++; continue; }

                        string outPath = Path.Combine(dest, name.Replace('/', '\\'));
                        if (entry.FullName.EndsWith("/"))
                        { Directory.CreateDirectory(outPath); continue; }

                        string outDir = Path.GetDirectoryName(outPath);
                        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                            Directory.CreateDirectory(outDir);

                        using (var outFs = File.Create(outPath))
                        using (var inS = entry.Open())
                            inS.CopyTo(outFs);
                    }
                }

                result.Ok = true;
                result.Output = dest;
                result.Message = "解压完成: " + dest + (skipped > 0 ? "（已跳过 " + skipped + " 个不安全条目）" : "");
            }
            catch (Exception ex)
            {
                result.Message = "解压失败: " + ex.Message;
                if (dest.Length > 0 && Directory.Exists(dest))
                { try { Directory.Delete(dest, true); } catch { } }
            }
            return result;
        }
    }

    public class MainWindow : Window
    {
        static string[] PendingFiles = new string[0];

        Border dropZone;
        TextBlock status;
        ListBox logList;
        CheckBox autoOpenChk, topMostChk;
        string lastOutput = "";

        public MainWindow()
        {
            Title = "拖拽解压助手";
            Width = 480; Height = 620;
            MinWidth = 420; MinHeight = 500;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
            AllowDrop = true;
            Topmost = true;

            BuildUi();
            HookEvents();

            if (PendingFiles.Length > 0)
                Dispatcher.BeginInvoke(new Action(() => ProcessFiles(PendingFiles)));
        }

        void BuildUi()
        {
            var grid = new Grid { Margin = new Thickness(18) };
            for (int i = 0; i < 5; i++) grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
            Content = grid;

            // 标题区
            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            header.Children.Add(new TextBlock
            {
                Text = "拖拽解压助手", FontSize = 22,
                FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37))
            });
            header.Children.Add(new TextBlock
            {
                Text = "把 zip 拖到下方区域，自动按正确编码解压并清理 macOS 垃圾",
                FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            Grid.SetRow(header, 0); grid.Children.Add(header);

            // 拖拽区
            dropZone = new Border
            {
                AllowDrop = true, Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var zone = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            zone.Children.Add(new TextBlock { Text = "\U0001F4E6", FontSize = 54, HorizontalAlignment = HorizontalAlignment.Center });
            zone.Children.Add(new TextBlock
            {
                Text = "把 zip 拖到这里", FontSize = 18, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51)),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0)
            });
            zone.Children.Add(new TextBlock
            {
                Text = "支持一次拖多个 · 自动清理 __MACOSX", FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0)
            });
            var pick = new TextBlock
            {
                Text = "也可以点这里选择文件", FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0),
                Cursor = Cursors.Hand
            };
            pick.MouseLeftButtonUp += (s, e) => PickFiles();
            zone.Children.Add(pick);
            dropZone.Child = zone;
            Grid.SetRow(dropZone, 1); grid.Children.Add(dropZone);

            // 状态
            status = new TextBlock
            {
                Text = "就绪", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                Margin = new Thickness(2, 0, 0, 8), TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(status, 2); grid.Children.Add(status);

            // 日志
            logList = new ListBox
            {
                Height = 170, BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
                BorderThickness = new Thickness(1), Background = Brushes.White, FontSize = 12
            };
            Grid.SetRow(logList, 3); grid.Children.Add(logList);

            // 按钮区
            var btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var openBtn = new Button
            {
                Content = "打开输出文件夹", Padding = new Thickness(12, 6, 12, 6),
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB)),
                Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand
            };
            openBtn.Click += (s, e) => { if (lastOutput.Length > 0) Process.Start("explorer.exe", "\"" + lastOutput + "\""); };
            var clearBtn = new Button
            {
                Content = "清空记录", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand
            };
            clearBtn.Click += (s, e) => logList.Items.Clear();
            autoOpenChk = new CheckBox { Content = "解压后自动打开文件夹", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            topMostChk = new CheckBox { Content = "窗口置顶", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            topMostChk.Checked += (s, e) => Topmost = true;
            topMostChk.Unchecked += (s, e) => Topmost = false;
            btns.Children.Add(openBtn);
            btns.Children.Add(clearBtn);
            btns.Children.Add(autoOpenChk);
            btns.Children.Add(topMostChk);
            Grid.SetRow(btns, 4); grid.Children.Add(btns);
        }

        void HookEvents()
        {
            DragEventHandler enterOver = (s, e) =>
            {
                e.Handled = true;
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.Copy;
                    DropHighlight(true);
                }
                else e.Effects = DragDropEffects.None;
            };
            DragEnter += enterOver;
            DragOver += enterOver;
            dropZone.DragEnter += enterOver;
            dropZone.DragOver += enterOver;

            DragEventHandler leave = (s, e) => DropHighlight(false);
            DragLeave += leave;
            dropZone.DragLeave += leave;

            DragEventHandler drop = (s, e) =>
            {
                e.Handled = true;
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0) ProcessFiles(files);
                }
            };
            Drop += drop;
            dropZone.Drop += drop;
        }

        void PickFiles()
        {
            var ofd = new OpenFileDialog { Multiselect = true, Filter = "ZIP 压缩包 (*.zip)|*.zip|所有文件 (*.*)|*.*" };
            if (ofd.ShowDialog(this) == true) ProcessFiles(ofd.FileNames);
        }

        void DropHighlight(bool on)
        {
            dropZone.BorderBrush = on ? Brushes.RoyalBlue : new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB));
            dropZone.BorderThickness = new Thickness(on ? 3 : 2);
        }

        void AddLog(string text, Color color)
        {
            logList.Items.Add(new ListBoxItem
            {
                Content = text,
                Foreground = new SolidColorBrush(color),
                Margin = new Thickness(0, 1, 0, 1)
            });
            logList.ScrollIntoView(logList.Items[logList.Items.Count - 1]);
        }

        void ProcessFiles(string[] paths)
        {
            DropHighlight(false);
            int ok = 0, fail = 0;
            foreach (string p in paths)
            {
                status.Text = "正在解压: " + Path.GetFileName(p) + " ...";
                var r = ZipExtractor.Extract(p);
                if (r.Ok)
                {
                    ok++;
                    lastOutput = r.Output;
                    AddLog("OK  " + r.Message, Colors.ForestGreen);
                    if (autoOpenChk.IsChecked == true)
                        Process.Start("explorer.exe", "\"" + r.Output + "\"");
                }
                else
                {
                    fail++;
                    AddLog("FAIL  " + r.Message, Colors.Firebrick);
                }
            }
            if (ok + fail > 0) status.Text = "完成: " + ok + " 个成功，" + fail + " 个失败/跳过";
        }

        public static void RunWith(string[] files)
        {
            PendingFiles = files;
            var app = new Application();
            app.Run(new MainWindow());
        }
    }

    public static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest" && args.Length > 1)
            {
                var r = ZipExtractor.Extract(args[1]);
                Console.WriteLine(r.Message);
                return;
            }
            if (args.Length > 0 && args[0] == "--debug" && args.Length > 1)
            {
                try
                {
                    var names = ZipExtractor.ReadZipNames(args[1]);
                    Console.WriteLine("解析成功, " + names.Count + " 个条目:");
                    foreach (var n in names.Take(6)) Console.WriteLine("  " + n);
                }
                catch (Exception ex) { Console.WriteLine("DEBUG失败: " + ex.Message); }
                return;
            }
            string[] files = args.Where(a => File.Exists(a)).ToArray();
            MainWindow.RunWith(files);
        }
    }
}
