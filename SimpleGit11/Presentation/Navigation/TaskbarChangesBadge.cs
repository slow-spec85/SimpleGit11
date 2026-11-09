using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace SimpleGit11.Presentation.Navigation;

internal sealed class TaskbarChangesBadge : IDisposable
{
    private const string ExplorerAdvancedKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const int NeverCombine = 2;
    private const int BaseIconSize = 16;
    private const uint DefaultDpi = 96;
    private const int BadgeScale = 4;
    private const int BadgeBackground = 0x26252d;
    private const uint WindowNonClientDestroy = 0x0082;
    private static readonly Guid TaskbarListClass = new("56FDF344-FD6D-11D0-958A-006097C9A090");
    private static readonly Guid TaskbarList3Interface = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");
    private static readonly string WindowPropertyPrefix = "SimpleGit11.Changes."
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            (Environment.ProcessPath ?? "").ToUpperInvariant())));
    private static readonly string CountProperty = WindowPropertyPrefix + ".Count";
    private static readonly string WorktreeProperty = WindowPropertyPrefix + ".Worktree";

    private readonly nint _window;
    private readonly Func<long, string> _describeCount;
    private readonly SubclassProcedure _windowProcedure;
    private readonly uint _taskbarButtonCreated = RegisterWindowMessage("TaskbarButtonCreated");
    private readonly bool _subclassInstalled;
    private bool _taskbarButtonReady;
    private nint _taskbarList;
    private int _localCount;
    private nint _worktree;
    private (long Count, int IconSize)? _displayedBadge;

    public TaskbarChangesBadge(nint window, Func<long, string> describeCount)
    {
        _window = window;
        _describeCount = describeCount;
        _windowProcedure = WindowProcedure;
        _subclassInstalled = SetWindowSubclass(window, _windowProcedure, 1, 0);
    }

    public void Publish(string? worktreeIdentity, int count)
    {
        nint worktree = worktreeIdentity is null ? 0
            : (nint)BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes(worktreeIdentity)));
        if (_worktree == worktree && _localCount == count)
        {
            return;
        }

        _localCount = count;
        if (_worktree != worktree)
        {
            _worktree = worktree;
            RemoveProp(_window, CountProperty);
            if (worktree != 0)
            {
                SetProp(_window, WorktreeProperty, worktree);
            }
            else
            {
                RemoveProp(_window, WorktreeProperty);
            }
        }

        if (count > 0)
        {
            SetProp(_window, CountProperty, count);
        }
        else
        {
            RemoveProp(_window, CountProperty);
        }
    }

    public void Refresh()
    {
        if (!_taskbarButtonReady)
        {
            return;
        }

        long count = GetGroupingMode() == NeverCombine ? _localCount : GetCombinedCount();
        // Explorer can show the same overlay on the primary taskbar and on the window's monitor.
        int iconSize = count > 0 ? GetBadgeIconSize(Math.Max(GetDpiForSystem(), GetDpiForWindow(_window))) : 0;
        if (_displayedBadge != (count, iconSize) && SetOverlay(count, iconSize))
        {
            _displayedBadge = (count, iconSize);
        }
    }

    public void Dispose()
    {
        Publish(null, 0);
        if (_taskbarList != 0)
        {
            if (_taskbarButtonReady)
            {
                SetOverlay(0, 0);
            }

            Marshal.Release(_taskbarList);
            _taskbarList = 0;
        }

        if (_subclassInstalled)
        {
            RemoveWindowSubclass(_window, _windowProcedure, 1);
        }
    }

    private static int GetGroupingMode()
    {
        try
        {
            return Registry.GetValue(ExplorerAdvancedKey, "TaskbarGlomLevel", 0) is int mode ? mode : 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return 0;
        }
    }

    private long GetCombinedCount()
    {
        List<(nint Worktree, int Count)> counts = [];
        if (_localCount > 0)
        {
            counts.Add((_worktree == 0 ? _window : _worktree, _localCount));
        }

        EnumWindows((window, _) =>
        {
            long count = GetProp(window, CountProperty);
            if (window != _window && count is > 0 and <= int.MaxValue)
            {
                nint worktree = GetProp(window, WorktreeProperty);
                counts.Add((worktree == 0 ? window : worktree, (int)count));
            }

            return true;
        }, 0);

        return CombineWorktreeCounts(counts);
    }

    internal static long CombineWorktreeCounts(IEnumerable<(nint Worktree, int Count)> counts)
    {
        Dictionary<nint, int> worktrees = new();
        foreach ((nint worktree, int count) in counts)
        {
            worktrees[worktree] = Math.Max(worktrees.GetValueOrDefault(worktree), count);
        }

        long total = 0;
        foreach (int count in worktrees.Values)
        {
            total += count;
        }

        return total;
    }

    private nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data)
    {
        if (message == WindowNonClientDestroy)
        {
            _taskbarButtonReady = false;
            RemoveProp(window, CountProperty);
            RemoveProp(window, WorktreeProperty);
            RemoveWindowSubclass(window, _windowProcedure, id);
        }
        else if (_taskbarButtonCreated != 0 && message == _taskbarButtonCreated)
        {
            _taskbarButtonReady = true;
            _displayedBadge = null;
            if (_taskbarList != 0)
            {
                Marshal.Release(_taskbarList);
                _taskbarList = 0;
            }
        }

        return DefSubclassProc(window, message, wParam, lParam);
    }

    private unsafe bool SetOverlay(long count, int iconSize)
    {
        if (_taskbarList == 0)
        {
            if (CoCreateInstance(in TaskbarListClass, 0, 1, in TaskbarList3Interface, out nint taskbarList) < 0)
            {
                return false;
            }

            // ITaskbarList::HrInit is the first slot after IUnknown.
            nint* vtable = *(nint**)taskbarList;
            if (((delegate* unmanaged[Stdcall]<nint, int>)vtable[3])(taskbarList) < 0)
            {
                Marshal.Release(taskbarList);
                return false;
            }

            _taskbarList = taskbarList;
        }

        nint icon = count > 0 ? CreateBadgeIcon(count, iconSize) : 0;
        if (count > 0 && icon == 0)
        {
            return false;
        }

        try
        {
            string? description = count > 0 ? _describeCount(count) : null;
            fixed (char* text = description)
            {
                // ITaskbarList3::SetOverlayIcon follows the thumbnail toolbar methods.
                nint* vtable = *(nint**)_taskbarList;
                var setOverlay = (delegate* unmanaged[Stdcall]<nint, nint, nint, char*, int>)vtable[18];
                return setOverlay(_taskbarList, _window, icon, text) >= 0;
            }
        }
        finally
        {
            if (icon != 0)
            {
                DestroyIcon(icon);
            }
        }
    }

    internal static int GetBadgeIconSize(uint dpi) =>
        (int)((BaseIconSize * Math.Max(dpi, DefaultDpi) + DefaultDpi / 2) / DefaultDpi);

    private static unsafe nint CreateBadgeIcon(long count, int iconSize)
    {
        uint[]? badge = RenderBadgePixels(count, iconSize);
        if (badge is null)
        {
            return 0;
        }

        nint bitmap = CreateBitmapSurface(iconSize, out nint bits);
        if (bitmap == 0)
        {
            return 0;
        }

        nint mask = 0;
        try
        {
            badge.AsSpan().CopyTo(new Span<uint>((void*)bits, badge.Length));
            int maskStride = (iconSize + 15) / 16 * 2; // A monochrome bitmap row is aligned to 16 bits.
            mask = CreateBitmap(iconSize, iconSize, 1, 1, new byte[iconSize * maskStride]);
            return mask == 0 ? 0 : CreateIconIndirect(new IconInfo { IsIcon = 1, Mask = mask, Color = bitmap });
        }
        finally
        {
            if (mask != 0)
            {
                DeleteObject(mask);
            }

            DeleteObject(bitmap);
        }
    }

    internal static unsafe uint[]? RenderBadgePixels(long count, int iconSize)
    {
        int size = iconSize * BadgeScale;
        const int boldWeight = 700;
        const uint grayscaleAntialiasing = 4;
        const uint centeredSingleLine = 0x0001 | 0x0004 | 0x0020; // DT_CENTER | DT_VCENTER | DT_SINGLELINE
        string label = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        nint bitmap = CreateBitmapSurface(size, out nint bits);
        nint dc = CreateCompatibleDC(0);
        int baseFontSize = label.Length switch { 1 => 14, 2 => 12, _ => 8 };
        int fontSize = (baseFontSize * size + BaseIconSize / 2) / BaseIconSize;
        nint font = CreateFont(-fontSize, 0, 0, 0, boldWeight,
            0, 0, 0, 1, 0, 0, grayscaleAntialiasing, 0, "Segoe UI");
        try
        {
            if (bitmap == 0 || dc == 0 || font == 0)
            {
                return null;
            }

            Span<uint> textMask = new((void*)bits, size * size);
            textMask.Clear();
            SelectObject(dc, bitmap);
            SelectObject(dc, font);
            SetBkMode(dc, 1); // TRANSPARENT
            SetTextColor(dc, 0xffffff);
            Rect bounds = new() { Right = size, Bottom = size };
            if (DrawText(dc, label, label.Length, ref bounds, centeredSingleLine) == 0)
            {
                return null;
            }

            GdiFlush();
            Rect ink = MeasureTextInk(textMask, size);
            int offsetX = (size - ink.Right + ink.Left) / 2 - ink.Left;
            int offsetY = (size - ink.Bottom + ink.Top) / 2 - ink.Top;
            return ComposeBadge(textMask, iconSize, label.Length == 1, offsetX, offsetY);
        }
        finally
        {
            // The memory DC owns the selection, so release it before deleting its bitmap and font.
            if (dc != 0) { DeleteDC(dc); }
            if (font != 0) { DeleteObject(font); }
            if (bitmap != 0) { DeleteObject(bitmap); }
        }
    }

    private static Rect MeasureTextInk(ReadOnlySpan<uint> textMask, int size)
    {
        Rect ink = new() { Left = size, Top = size };
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                if ((byte)textMask[y * size + x] == 0)
                {
                    continue;
                }

                ink.Left = Math.Min(ink.Left, x);
                ink.Top = Math.Min(ink.Top, y);
                ink.Right = Math.Max(ink.Right, x + 1);
                ink.Bottom = y + 1;
            }
        }

        return ink;
    }

    private static uint[] ComposeBadge(ReadOnlySpan<uint> textMask, int iconSize, bool circle, int offsetX, int offsetY)
    {
        int size = iconSize * BadgeScale;
        const int samplesPerPixel = BadgeScale * BadgeScale;
        double center = size / 2.0;
        double halfStraightSection = circle ? 0 : (double)size / BaseIconSize;
        double radius = center - halfStraightSection;
        uint[] badge = new uint[iconSize * iconSize];
        for (int y = 0; y < iconSize; y++)
        {
            for (int x = 0; x < iconSize; x++)
            {
                int covered = 0;
                int inkCoverage = 0;
                for (int sampleY = y * BadgeScale; sampleY < (y + 1) * BadgeScale; sampleY++)
                {
                    double dy = sampleY + 0.5 - center;
                    int textY = sampleY - offsetY;
                    for (int sampleX = x * BadgeScale; sampleX < (x + 1) * BadgeScale; sampleX++)
                    {
                        double dx = Math.Max(0, Math.Abs(sampleX + 0.5 - center) - halfStraightSection);
                        if (dx * dx + dy * dy <= radius * radius)
                        {
                            covered++;
                            int textX = sampleX - offsetX;
                            if ((uint)textX < (uint)size && (uint)textY < (uint)size)
                            {
                                inkCoverage += (byte)textMask[textY * size + textX];
                            }
                        }
                    }
                }

                // Average premultiplied pixels to preserve smooth edges on any taskbar background.
                uint alpha = (uint)((255 * covered + samplesPerPixel / 2) / samplesPerPixel);
                uint red = BlendChannel(BadgeBackground >> 16, covered, inkCoverage);
                uint green = BlendChannel((BadgeBackground >> 8) & 0xff, covered, inkCoverage);
                uint blue = BlendChannel(BadgeBackground & 0xff, covered, inkCoverage);
                badge[y * iconSize + x] = (alpha << 24) | (red << 16) | (green << 8) | blue;
            }
        }

        return badge;

        static uint BlendChannel(int background, int covered, int inkCoverage)
        {
            int brightness = background * covered + (255 - background) * inkCoverage / 255;
            return (uint)((brightness + samplesPerPixel / 2) / samplesPerPixel);
        }
    }

    private static unsafe nint CreateBitmapSurface(int size, out nint bits)
    {
        BitmapInfoHeader header = new()
        {
            Size = (uint)sizeof(BitmapInfoHeader),
            Width = size,
            Height = -size,
            Planes = 1,
            BitsPerPixel = 32
        };
        return CreateDIBSection(0, in header, 0, out bits, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitsPerPixel;
        public uint Compression;
        public uint SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public uint XHotspot;
        public uint YHotspot;
        public nint Mask;
        public nint Color;
    }

    private delegate nint SubclassProcedure(nint window, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    private delegate bool WindowEnumerator(nint window, nint data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure callback, nuint id, nuint data);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure callback, nuint id);

    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetProp(nint window, string name, nint value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetProp(nint window, string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint RemoveProp(nint window, string name);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool EnumWindows(WindowEnumerator callback, nint data);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateDIBSection(nint dc, in BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, byte[] bits);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool DeleteObject(nint handle);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool DeleteDC(nint dc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CreateFont(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string faceName);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern nint SelectObject(nint dc, nint handle);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int SetBkMode(nint dc, int mode);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern uint SetTextColor(nint dc, uint color);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern bool GdiFlush();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int DrawText(nint dc, string text, int length, ref Rect bounds, uint format);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint CreateIconIndirect(in IconInfo iconInfo);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyIcon(nint icon);
}
