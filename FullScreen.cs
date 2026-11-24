using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace AmosLikeBasic;

// F12: a window to full screen and back. The switch (an animation into a space of its own on macOS)
// swallows the key's release and takes the keyboard from the window, so the keys held are forgotten
// (or F12 would stay down for INKEY$ and KEYSTATE) and the window is given the keyboard again after it.
public static class FullScreen
{
    public static void Toggle(Window window, Action forgetKeys)
    {
        forgetKeys();
        window.WindowState = window.WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
        void Refocus()
        {
            if (!window.IsVisible) return;
            window.Activate();
            window.Focus();
            forgetKeys();
        }
        Dispatcher.UIThread.Post(Refocus, DispatcherPriority.Background);
        DispatcherTimer.RunOnce(Refocus, TimeSpan.FromMilliseconds(800));
    }
}
