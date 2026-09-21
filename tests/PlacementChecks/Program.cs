using System.Drawing;
using VirtualDesktopBar;

int checks = 0;
void Check(string name, Point expected, Rectangle screen, Rectangle work, Rectangle? taskbar,
    uint edge = 3, int width = 300, int height = 52, int margin = 10, bool above = false)
{
    var actual = BarPlacement.Calculate(screen, work, taskbar, edge, width, height, margin, above);
    if (actual != expected) throw new Exception($"{name}: expected {expected}, got {actual}");
    checks++;
}

var screen = new Rectangle(0, 0, 1920, 1080);
var work = new Rectangle(0, 0, 1920, 1032);
var taskbar = new Rectangle(0, 1032, 1920, 48);
Check("Overlay stays within screen even when bar is taller than taskbar", new(10, 1028), screen, work, taskbar);
Check("Above uses actual taskbar boundary", new(10, 980), screen, work, taskbar, above: true);
Check("Tall taskbar centers overlay", new(10, 1006), screen, work, new(0, 984, 1920, 96));
Check("Missing taskbar uses work area", new(10, 980), screen, work, null);
Check("Auto-hide above still reserves taskbar height", new(10, 980), screen, screen, taskbar, above: true);
Check("150 percent pixels", new(15, 1470), new(0, 0, 2880, 1620), new(0, 0, 2880, 1548),
    new(0, 1548, 2880, 72), width: 450, height: 78, margin: 15, above: true);
Check("Negative monitor origin", new(-1910, 980), new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1032),
    new(-1920, 1032, 1920, 48), above: true);
Check("Oversized bar is anchored onscreen", new(0, 1028), screen, work, taskbar, width: 2200);
Check("Top taskbar alternative is on desktop side", new(10, 48), screen, new(0, 48, 1920, 1032),
    new(0, 0, 1920, 48), edge: 1, above: true);
Check("Left taskbar alternative is on desktop side", new(58, 1028), screen, new(48, 0, 1872, 1080),
    new(0, 0, 48, 1080), edge: 0, above: true);
Check("Right taskbar alternative is on desktop side", new(1562, 1028), screen, new(0, 0, 1872, 1080),
    new(1872, 0, 48, 1080), edge: 2, above: true);
Console.WriteLine($"Passed {checks} placement checks.");
