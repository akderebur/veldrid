using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using static SDL2.SDL;

namespace Veldrid.Sdl2
{
    internal static class Sdl2WindowRegistry
    {
        public static readonly object Lock = new object();
        private static readonly Dictionary<uint, Sdl2Window> _eventsByWindowID
            = new Dictionary<uint, Sdl2Window>();
        private static bool _firstInit;

        public static void RegisterWindow(Sdl2Window window)
        {
            lock (Lock)
            {
                _eventsByWindowID.Add(window.WindowID, window);
                if (!_firstInit)
                {
                    _firstInit = true;
                    Sdl2Events.Subscribe(ProcessWindowEvent);
                }
            }
        }

        public static void RemoveWindow(Sdl2Window window)
        {
            lock (Lock)
            {
                _eventsByWindowID.Remove(window.WindowID);
            }
        }

        private static void ProcessWindowEvent(ref SDL_Event ev)
        {
            bool handled = false;
            uint windowID = 0;
            switch (ev.type)
            {
                case SDL_EventType.SDL_QUIT:
                case SDL_EventType.SDL_WINDOWEVENT:
                case SDL_EventType.SDL_KEYDOWN:
                case SDL_EventType.SDL_KEYUP:
                case SDL_EventType.SDL_TEXTEDITING:
                case SDL_EventType.SDL_TEXTINPUT:
                case SDL_EventType.SDL_KEYMAPCHANGED:
                case SDL_EventType.SDL_MOUSEMOTION:
                case SDL_EventType.SDL_MOUSEBUTTONDOWN:
                case SDL_EventType.SDL_MOUSEBUTTONUP:
                case SDL_EventType.SDL_MOUSEWHEEL:
                    windowID = ev.window.windowID;
                    handled = true;
                    break;
                case SDL_EventType.SDL_DROPBEGIN:
                case SDL_EventType.SDL_DROPCOMPLETE:
                case SDL_EventType.SDL_DROPFILE:
                case SDL_EventType.SDL_DROPTEXT:
                    SDL_DropEvent dropEvent = ev.drop;
                    windowID = dropEvent.windowID;
                    handled = true;
                    break;
                default:
                    handled = false;
                    break;
            }

            if (handled && _eventsByWindowID.TryGetValue(windowID, out Sdl2Window window))
            {
                window.AddEvent(ev);
            }
        }
    }
}
