using System;
using System.Drawing;

namespace VirtualDesktopBar;

internal static class BarPlacement
{
    // Shell edge values: left=0, top=1, right=2, bottom=3.
    internal static Point Calculate(Rectangle screen, Rectangle workArea, Rectangle? taskbar,
        uint edge, int width, int height, int margin, bool above)
    {
        int x = workArea.Left + margin;
        int y = workArea.Bottom - height;
        if (taskbar is Rectangle bar)
        {
            if (edge == 3)
            {
                x = bar.Left + margin;
                y = above ? bar.Top - height : bar.Top + (bar.Height - height) / 2;
            }
            else if (edge == 1)
            {
                x = bar.Left + margin;
                y = above ? bar.Bottom : bar.Top + (bar.Height - height) / 2;
            }
            else
            {
                // For vertical taskbars, the alternative mode sits on the desktop side.
                x = above ? (edge == 0 ? bar.Right + margin : bar.Left - width - margin)
                    : bar.Left + (bar.Width - width) / 2;
                y = bar.Bottom - height;
            }
        }
        return new Point(Math.Clamp(x, screen.Left, Math.Max(screen.Left, screen.Right - width)),
            Math.Clamp(y, screen.Top, Math.Max(screen.Top, screen.Bottom - height)));
    }
}
