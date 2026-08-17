using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Menu cursor via CursorManager so Unity Cursor.visible stays in sync.
    /// Vanilla cockpit/orbit look only reads Pan/Tilt while !Cursor.visible;
    /// writing Cursor.visible directly left the manager thinking the cursor
    /// was hidden, so gamepad look stayed dead after any Oritasy menu.
    /// Unused flag bit — vanilla stops at EmptyScene = 0x100.
    /// </summary>
    internal static class OritasyCursor
    {
        private const CursorFlags MenuFlag = (CursorFlags)0x200;

        private static int _holds;

        internal static bool Held
        {
            get { return _holds > 0; }
        }

        internal static void Hold()
        {
            _holds++;
            if (_holds == 1)
            {
                try { CursorManager.SetFlag(MenuFlag, true); }
                catch { }
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        internal static void Pulse()
        {
            if (_holds <= 0)
                return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        internal static void Release()
        {
            if (_holds <= 0)
                return;
            _holds--;
            if (_holds > 0)
                return;
            try { CursorManager.SetFlag(MenuFlag, false); }
            catch { }
            SyncToManager();
        }

        internal static void SyncToManager()
        {
            try { CursorManager.Refresh(); }
            catch { }
            bool show = false;
            try { show = CursorManager.Visible; }
            catch { show = false; }
            Cursor.visible = show;
            Cursor.lockState = show ? CursorLockMode.None : CursorLockMode.Locked;
        }

        internal static void SyncIfDesynced()
        {
            if (_holds > 0)
                return;
            bool shouldShow = false;
            try { shouldShow = CursorManager.Visible; }
            catch { return; }
            CursorLockMode wantLock = shouldShow ? CursorLockMode.None : CursorLockMode.Locked;
            if (Cursor.visible == shouldShow && Cursor.lockState == wantLock)
                return;
            SyncToManager();
        }
    }
}
