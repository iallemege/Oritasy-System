using System;
using System.Reflection;

namespace Oritasy
{
    /// <summary>
    /// Forwards baas progress to BIA.Runtime's boot bar. Oritasy does not draw it.
    /// </summary>
    internal static class BiaBoot
    {
        private static bool _tried;
        private static Type _t;
        private static MethodInfo _setStatus;
        private static MethodInfo _setRow;
        private static MethodInfo _markDone;
        private static PropertyInfo _visible;

        internal static bool Visible
        {
            get
            {
                Ensure();
                if (_visible == null)
                    return false;
                try
                {
                    object v = _visible.GetValue(null, null);
                    return v is bool && (bool)v;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static void SetStatus(string en, string zh)
        {
            Ensure();
            if (_setStatus == null)
                return;
            string text = UiLang.T(en, zh);
            try { _setStatus.Invoke(null, new object[] { text }); }
            catch { }
        }

        internal static void SetRow(string name, int current, int total, bool failed)
        {
            Ensure();
            if (_setRow == null)
                return;
            try
            {
                _setRow.Invoke(null, new object[] { name, current, total, failed });
            }
            catch { }
        }

        internal static void MarkDone()
        {
            Ensure();
            if (_markDone == null)
                return;
            try { _markDone.Invoke(null, null); }
            catch { }
        }

        private static void Ensure()
        {
            if (_tried)
                return;
            _tried = true;
            try
            {
                _t = Type.GetType("BIA.BIALoadScreen, BIA.Runtime", false);
            }
            catch
            {
                _t = null;
            }
            if (_t == null)
                return;
            BindingFlags f = BindingFlags.Public | BindingFlags.Static;
            _setStatus = _t.GetMethod("SetStatus", f);
            _setRow = _t.GetMethod("SetRow", f);
            _markDone = _t.GetMethod("MarkDone", f);
            _visible = _t.GetProperty("Visible", f);
        }
    }
}
