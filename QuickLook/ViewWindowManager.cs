﻿using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using QuickLook.Helpers;
using QuickLook.Plugin;

namespace QuickLook
{
    internal class ViewWindowManager : IDisposable
    {
        private static ViewWindowManager _instance;

        private readonly MainWindowNoTransparent _viewWindowNoTransparent;
        private readonly MainWindowTransparent _viewWindowTransparentTransparent;
        private MainWindowTransparent _currentMainWindow;

        private string _path = string.Empty;

        internal ViewWindowManager()
        {
            _viewWindowTransparentTransparent = new MainWindowTransparent();
            _viewWindowNoTransparent = new MainWindowNoTransparent();

            _currentMainWindow = _viewWindowTransparentTransparent;
        }

        public void Dispose()
        {
            StopFocusMonitor();
        }

        internal void InvokeRoutine(KeyEventArgs kea, bool isKeyDown)
        {
            Debug.WriteLine($"InvokeRoutine: key={kea.KeyCode},down={isKeyDown}");


            if (isKeyDown)
                switch (kea.KeyCode)
                {
                    case Keys.Enter:
                        RunAndClosePreview();
                        break;
                }
            else
                switch (kea.KeyCode)
                {
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Left:
                    case Keys.Right:
                        SwitchPreview();
                        break;
                    case Keys.Space:
                        TogglePreview();
                        break;
                    case Keys.Escape:
                        ClosePreview();
                        break;
                }
        }

        internal void RunAndClosePreview()
        {
            if (_currentMainWindow.Visibility != Visibility.Visible)
                return;

            // if the current focus is in Desktop or explorer windows, just close the preview window and leave the task to System.
            var focus = NativeMethods.QuickLook.GetFocusedWindowType();
            if (focus != NativeMethods.QuickLook.FocusedWindowType.Invalid)
            {
                StopFocusMonitor();
                _currentMainWindow.BeginHide();
                return;
            }

            // if the focus is in the preview window, run it
            if (!WindowHelper.IsForegroundWindowBelongToSelf())
                return;

            StopFocusMonitor();
            _currentMainWindow.RunAndHide();
        }

        internal void ClosePreview()
        {
            if (_currentMainWindow.Visibility != Visibility.Visible)
                return;

            StopFocusMonitor();
            _currentMainWindow.BeginHide();
        }

        private void TogglePreview()
        {
            if (_currentMainWindow.Visibility == Visibility.Visible)
            {
                ClosePreview();
            }
            else
            {
                _path = NativeMethods.QuickLook.GetCurrentSelection();
                InvokeViewer();
            }
        }

        private void SwitchPreview()
        {
            if (_currentMainWindow.Visibility != Visibility.Visible)
                return;

            // if the switch has been done by SwitchPreviewRemoteInvoke, we'll not do anything
            var select = NativeMethods.QuickLook.GetCurrentSelection();
            if (_path == select)
                return;

            _path = select;

            Debug.WriteLine($"SwitchPreview: {_path}");

            InvokeViewer();
        }

        private void SwitchPreviewRemoteInvoke(HeartbeatEventArgs e)
        {
            // if the switch has been done by SwitchPreview, we'll not do anything
            if (e.FocusedFile == _path)
                return;

            Debug.WriteLine($"SwitchPreviewRemoteInvoke: {e.FocusedFile}");

            if (string.IsNullOrEmpty(e.FocusedFile))
                return;

            _currentMainWindow?.Dispatcher.BeginInvoke(new Action(SwitchPreview), DispatcherPriority.ApplicationIdle);
        }

        private void RunFocusMonitor()
        {
            if (!FocusMonitor.GetInstance().IsRunning)
            {
                FocusMonitor.GetInstance().Start();
                FocusMonitor.GetInstance().Heartbeat += SwitchPreviewRemoteInvoke;
            }
        }

        private void StopFocusMonitor()
        {
            if (FocusMonitor.GetInstance().IsRunning)
            {
                FocusMonitor.GetInstance().Stop();
                FocusMonitor.GetInstance().Heartbeat -= SwitchPreviewRemoteInvoke;
            }
        }

        internal bool InvokeViewer(string path = null, bool topMost = true)
        {
            if (path != null)
                _path = path;

            if (string.IsNullOrEmpty(_path))
                return false;
            if (!Directory.Exists(_path) && !File.Exists(_path))
                return false;

            RunFocusMonitor();

            var matchedPlugin = PluginManager.GetInstance().FindMatch(_path);

            BeginShowNewWindow(matchedPlugin, topMost);

            return true;
        }

        private void BeginShowNewWindow(IViewer matchedPlugin, bool topMost = true)
        {
            _currentMainWindow.UnloadPlugin();

            // switch window
            var oldWindow = _currentMainWindow;
            _currentMainWindow = matchedPlugin.AllowsTransparency
                ? _viewWindowTransparentTransparent
                : _viewWindowNoTransparent;
            if (!ReferenceEquals(oldWindow, _currentMainWindow))
                oldWindow.BeginHide();

            _currentMainWindow.Topmost = !Debugger.IsAttached && topMost;
            _currentMainWindow.BeginShow(matchedPlugin, _path, CurrentPluginFailed);
        }

        private void CurrentPluginFailed(ExceptionDispatchInfo e)
        {
            var plugin = _currentMainWindow.Plugin.GetType();

            _currentMainWindow.BeginHide();

            TrayIconManager.GetInstance().ShowNotification("", $"Failed to preview {Path.GetFileName(_path)}", true);

            Debug.WriteLine(e.ToString());
            Debug.WriteLine(e.SourceException.StackTrace);

            if (plugin != PluginManager.GetInstance().DefaultPlugin.GetType())
                BeginShowNewWindow(PluginManager.GetInstance().DefaultPlugin);
            else
                e.Throw();
        }

        internal static ViewWindowManager GetInstance()
        {
            return _instance ?? (_instance = new ViewWindowManager());
        }
    }
}