using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// UH-90 Ibis: toggle cabin first-person guns while still flying the helicopter.
    /// </summary>
    internal static class InfantryCabinGunService
    {
        private static ConfigEntry<KeyCode> _key;
        private static bool _on;
        private static Camera _bound;

        internal static ConfigEntry<KeyCode> CabinKey
        {
            get { return _key; }
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _key = config.Bind("Infantry", "IbisCabinGunKey", KeyCode.G,
                "UH-90 Ibis: toggle first-person infantry guns while flying.");
        }

        internal static bool On
        {
            get { return _on; }
        }

        internal static bool BlockingAircraftGuns()
        {
            return _on && !InfantryModeService.Active;
        }

        internal static KeyCode Key()
        {
            return _key != null ? _key.Value : KeyCode.G;
        }

        internal static void Tick()
        {
            if (InfantryModeService.Active || !InfantryModeService.IsEnabled())
            {
                Stop();
                return;
            }
            if (OritasyPresentation.MainMenuActive || OritasyPresentation.SplashActive)
            {
                Stop();
                return;
            }
            if (!LocalIsIbis())
            {
                Stop();
                return;
            }
            if (Input.GetKeyDown(Key()) && !AircraftManeuverGui.IsRebinding
                && !InfantryHud.BlocksLook)
            {
                if (_on)
                    Stop();
                else
                    Start();
            }
            if (!_on)
                return;
            Camera cam = Camera.main;
            if (cam == null)
                return;
            if (_bound != cam)
            {
                InfantryCombat.AttachViews(cam.transform);
                _bound = cam;
            }
            InfantryCombat.Tick(cam.transform, Time.deltaTime);
        }

        internal static void Draw()
        {
            if (InfantryModeService.Active)
                return;
            if (!_on)
            {
                if (LocalIsIbis())
                {
                    GUI.Label(new Rect(18f, Screen.height - 36f, 560f, 22f),
                        UiLang.T("G — Ibis cabin guns (first person)",
                            "G — 朱鹮舱内第一人称机枪"));
                }
                return;
            }
            string gun = InfantryCombat.HudLine();
            GUI.Label(new Rect(18f, Screen.height - 48f, 640f, 22f),
                UiLang.T("IBIS GUN  ", "朱鹮机枪  ") + gun
                    + UiLang.T("   [" + Key().ToString() + " exit]",
                        "   [" + Key().ToString() + " 退出]"));
        }

        internal static void Stop()
        {
            if (!_on && _bound == null)
                return;
            _on = false;
            _bound = null;
            InfantryCombat.DestroyViews();
            InfantryCombat.ClearBolts();
        }

        private static void Start()
        {
            InfantryCombat.ApplyCarry(InfantryCombat.LoadCarryGun());
            Camera cam = Camera.main;
            if (cam != null)
            {
                InfantryCombat.AttachViews(cam.transform);
                _bound = cam;
            }
            _on = true;
        }

        internal static bool LocalIsIbis()
        {
            Aircraft ac;
            try
            {
                if (!GameManager.GetLocalAircraft(out ac) || ac == null)
                    return false;
            }
            catch
            {
                return false;
            }
            string key = "";
            try
            {
                if (ac.definition != null && ac.definition.jsonKey != null)
                    key = ac.definition.jsonKey;
                if (string.IsNullOrEmpty(key) && ac.definition != null)
                    key = ac.definition.unitName;
                if (string.IsNullOrEmpty(key))
                    key = ac.name;
            }
            catch { }
            return AircraftIdentity.IsUh90(key);
        }
    }
}
