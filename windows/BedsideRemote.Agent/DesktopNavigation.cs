using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;

namespace BedsideRemote.Agent;

internal static class DesktopNavigation
{
    internal static int Nearest(IReadOnlyList<Rect> bounds, int current, string direction)
    {
        if (bounds.Count == 0) return -1;
        if (current < 0) return 0;
        var origin = bounds[current];
        var horizontal = direction is "left" or "right";
        var positive = direction is "right" or "down";
        var result = current; var best = double.PositiveInfinity;
        for (var index = 0; index < bounds.Count; index++)
        {
            if (index == current) continue;
            var candidate = bounds[index];
            var dx = candidate.X + candidate.Width / 2 - origin.X - origin.Width / 2;
            var dy = candidate.Y + candidate.Height / 2 - origin.Y - origin.Height / 2;
            var forward = (horizontal ? dx : dy) * (positive ? 1 : -1);
            if (forward <= 1) continue;
            var sideways = Math.Abs(horizontal ? dy : dx);
            var overlap = horizontal ? candidate.Bottom > origin.Top && candidate.Top < origin.Bottom : candidate.Right > origin.Left && candidate.Left < origin.Right;
            var score = forward + sideways * 3 + (overlap ? 0 : 10000);
            if (score < best) { best = score; result = index; }
        }
        return result;
    }
    public static void Execute(string key, CancellationToken token)
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) throw new InvalidOperationException("Focus an application before navigating.");
        try
        {
            var root = AutomationElement.FromHandle(window);
            var walker = TreeWalker.ControlViewWalker;
            var pending = new Queue<AutomationElement>(); pending.Enqueue(root);
            var elements = new List<AutomationElement>(); var bounds = new List<Rect>();
            var started = Stopwatch.StartNew(); var visited = 0;
            while (pending.TryDequeue(out var parent) && visited < 1000 && started.ElapsedMilliseconds < 700)
            {
                token.ThrowIfCancellationRequested();
                for (var element = walker.GetFirstChild(parent); element != null && visited++ < 1000; element = walker.GetNextSibling(element))
                {
                    var value = element.Current;
                    if (!value.IsOffscreen && value.IsEnabled)
                    {
                        pending.Enqueue(element);
                        var rectangle = value.BoundingRectangle;
                        if (value.IsKeyboardFocusable && !rectangle.IsEmpty && rectangle.Width > 1 && rectangle.Height > 1 &&
                            value.ControlType != ControlType.Document && value.ControlType != ControlType.Pane && value.ControlType != ControlType.Window)
                        { elements.Add(element); bounds.Add(rectangle); }
                    }
                }
            }
            var focused = AutomationElement.FocusedElement;
            var current = elements.FindIndex(element => Automation.Compare(element, focused));
            token.ThrowIfCancellationRequested();
            if (GetForegroundWindow() != window) throw new InvalidOperationException("Navigation stopped because the focused window changed.");
            if (key is "enter" or "escape")
            {
                if (current < 0) throw new InvalidOperationException("Select a visible control with the arrows first. This app may require the touchpad.");
                if (key == "escape") { DesktopInput.NavigationKey("escape"); return; }
                if (focused.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) ((InvokePattern)invoke).Invoke();
                else if (focused.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) ((TogglePattern)toggle).Toggle();
                else DesktopInput.NavigationKey("enter");
                return;
            }
            var next = Nearest(bounds, current, key);
            if (next < 0) throw new InvalidOperationException("This app exposes no selectable controls. Use the touchpad.");
            var target = elements[next];
            if (target.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection)) ((SelectionItemPattern)selection).Select();
            target.SetFocus();
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or COMException)
        { throw new InvalidOperationException("The selected control is unavailable. Try again or use the touchpad.", ex); }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
