using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oritasy
{
    internal enum InfantryGun
    {
        M16 = 0,
        Ak47 = 1,
        Pistol = 2,
        Shotgun = 3,
        Sniper = 4,
        At4 = 5,
        Rpg7 = 6,
        Rbs70 = 7,
        G11 = 8
    }

    /// <summary>
    /// Hangar infantry: up to 4 guns. Aircraft / chute / hop-out: one carry-on.
    /// </summary>
    internal static class InfantryCombat
    {
        internal const int GunCount = 9;
        internal const int MaxKit = 4;
        internal const int KitMaskAll = (1 << GunCount) - 1;
        internal const string PrefCarryKey = "Oritasy.Infantry.CarryGun";
        internal const string PrefKitKey = "Oritasy.Infantry.KitMask";

        private static readonly string[] NamesEn = new string[]
        {
            "M16", "AK-47", "Pistol", "Shotgun", "Sniper", "AT-4", "RPG-7", "RBS 70", "G11"
        };
        private static readonly string[] NamesZh = new string[]
        {
            "M16", "AK-47", "手枪", "霰弹枪", "狙击枪", "AT-4", "RPG-7", "RBS 70", "G11"
        };
        private static readonly string[] MeshFiles = new string[]
        {
            "rifle.obj", "ak47.obj", "pistol.obj", "shotgun.obj", "sniper.obj",
            "at4.obj", "rpg7.obj", "rbs70.obj", "g11.obj"
        };
        private static readonly string[] ViewNames = new string[]
        {
            "M16VM", "AkVM", "PistolVM", "SgVM", "SnpVM", "At4VM", "RpgVM", "RbsVM", "G11VM"
        };
        private static readonly Vector3[] ViewPos = new Vector3[]
        {
            new Vector3(0.16f, -0.14f, 0.40f),
            new Vector3(0.16f, -0.14f, 0.40f),
            new Vector3(0.14f, -0.12f, 0.34f),
            new Vector3(0.16f, -0.15f, 0.42f),
            new Vector3(0.16f, -0.15f, 0.44f),
            new Vector3(0.14f, -0.18f, 0.46f),
            new Vector3(0.14f, -0.19f, 0.46f),
            new Vector3(0.11f, -0.19f, 0.42f),
            new Vector3(0.15f, -0.13f, 0.38f)
        };
        private static readonly float[] ViewLen = new float[]
        {
            0.52f, 0.52f, 0.32f, 0.50f, 0.58f, 0.62f, 0.58f, 0.55f, 0.50f
        };

        private static Mesh[] _meshes;
        private static bool _meshesTried;
        private static int _warmGun = -1;
        private static string _meshDir;
        private static Shader _gunTexSh;
        private static int _shownGun = -99;
        private static bool _shownHide;
        private static readonly RaycastHit[] RayBuf = new RaycastHit[32];

        private static GameObject _viewRoot;
        private static GameObject[] _gunGo;
        private static Material _matOlive;
        private static Material _matBlack;
        private static Material _matGrey;
        private static Material _matTan;
        private static Material[] _gunMats;
        private static Texture2D[] _gunTex;

        internal static InfantryGun Gun;
        internal static int AllowedMask = KitMaskAll;
        internal static bool FullKit;
        internal static int[] Ammo = new int[GunCount];
        internal static int[] Reserve = new int[GunCount];
        internal static float ReloadUntil;
        internal static float NextShotAt;
        private static int _pendingReloadGun = -1;
        private static int _pendingReloadTake;
        internal static float Recoil;
        internal static float SpreadBloom;
        internal static float RecoilRecover;
        private static int _tracerN;
        internal static float Ads;
        internal static float Zoom;
        internal static float SniperLock;
        internal static Unit SniperTarget;
        internal static float RbsLock;
        internal static Unit RbsTarget;
        internal static string Status = "";
        private static float _aimScanAt;
        private static Unit _rbsScan;
        private static Unit _snpScan;

        private class Bolt
        {
            public GameObject Go;
            public Vector3 Vel;
            public float DieAt;
            public bool Homing;
            public bool GuideLook;
            public float GuideUntil;
            public Unit Target;
            public Transform SeekXf;
            public int Kind;
            public InfantryGun Shot;
            public Transform Ignore;
            public List<Vector3> Path;
        }

        private static readonly List<Bolt> Bolts = new List<Bolt>(8);

        internal static string GunName(InfantryGun g)
        {
            int i = (int)g;
            if (i < 0 || i >= GunCount)
                return "?";
            return UiLang.T(NamesEn[i], NamesZh[i]);
        }

        internal static int PenGrade(InfantryGun g)
        {
            if (g == InfantryGun.M16) return 4;
            if (g == InfantryGun.Ak47) return 5;
            if (g == InfantryGun.Pistol) return 2;
            if (g == InfantryGun.Shotgun) return 3;
            if (g == InfantryGun.Sniper) return 6;
            if (g == InfantryGun.At4) return 18;
            if (g == InfantryGun.Rpg7) return 16;
            if (g == InfantryGun.Rbs70) return 15;
            if (g == InfantryGun.G11) return 6;
            return 4;
        }

        internal static float Kinetic(InfantryGun g)
        {
            if (g == InfantryGun.M16) return 18f;
            if (g == InfantryGun.Ak47) return 22f;
            if (g == InfantryGun.Pistol) return 10f;
            if (g == InfantryGun.Shotgun) return 8f;
            if (g == InfantryGun.Sniper) return 32f;
            if (g == InfantryGun.At4) return 70f;
            if (g == InfantryGun.Rpg7) return 58f;
            if (g == InfantryGun.Rbs70) return 40f;
            if (g == InfantryGun.G11) return 26f;
            return 10f;
        }

        internal static bool IsPureKe(InfantryGun g)
        {
            return g == InfantryGun.M16 || g == InfantryGun.Ak47
                || g == InfantryGun.Pistol || g == InfantryGun.Shotgun
                || g == InfantryGun.Sniper || g == InfantryGun.G11;
        }

        internal static string DmgKind(InfantryGun g)
        {
            if (g == InfantryGun.At4 || g == InfantryGun.Rpg7)
                return "HEAT";
            if (g == InfantryGun.Rbs70)
                return "HE";
            return "KE";
        }

        internal static float PierceHp(InfantryGun g)
        {
            if (g == InfantryGun.M16) return 4.5f;
            if (g == InfantryGun.Ak47) return 5.5f;
            if (g == InfantryGun.Pistol) return 3f;
            if (g == InfantryGun.Shotgun) return 3.5f;
            if (g == InfantryGun.Sniper) return 9f;
            if (g == InfantryGun.At4) return 95f;
            if (g == InfantryGun.Rpg7) return 75f;
            if (g == InfantryGun.Rbs70) return 55f;
            if (g == InfantryGun.G11) return 7.5f;
            return 4f;
        }

        internal static float BlastHp(InfantryGun g)
        {
            if (g == InfantryGun.At4) return 140f;
            if (g == InfantryGun.Rpg7) return 110f;
            if (g == InfantryGun.Rbs70) return 40f;
            return 0f;
        }

        internal static bool TrySelect(InfantryGun g)
        {
            if (!IsAllowed(g))
                return false;
            if (g != Gun)
                CancelReload();
            Gun = g;
            Ads = 0f;
            RbsLock = 0f;
            RbsTarget = null;
            SniperLock = 0f;
            SniperTarget = null;
            EnsureView(g);
            return true;
        }

        internal static InfantryGun ClampGun(int i)
        {
            if (i < 0)
                i = 0;
            if (i >= GunCount)
                i = GunCount - 1;
            return (InfantryGun)i;
        }

        internal static InfantryGun LoadCarryGun()
        {
            int i = 0;
            try
            {
                if (PlayerPrefs.HasKey(PrefCarryKey))
                    i = PlayerPrefs.GetInt(PrefCarryKey, 0);
            }
            catch { }
            return ClampGun(i);
        }

        internal static void SaveCarryGun(InfantryGun g)
        {
            try
            {
                PlayerPrefs.SetInt(PrefCarryKey, (int)g);
                PlayerPrefs.Save();
            }
            catch { }
        }

        internal static int LoadKitMask()
        {
            int mask = KitMaskAll;
            try
            {
                if (PlayerPrefs.HasKey(PrefKitKey))
                    mask = PlayerPrefs.GetInt(PrefKitKey, KitMaskAll);
            }
            catch { }
            mask &= KitMaskAll;
            if (mask == 0)
                mask = KitMaskAll;
            return ClampKit(mask);
        }

        internal static void SaveKitMask(int mask)
        {
            mask = ClampKit(mask);
            if (mask == 0)
                mask = 1;
            try
            {
                PlayerPrefs.SetInt(PrefKitKey, mask);
                PlayerPrefs.Save();
            }
            catch { }
        }

        internal static bool KitHas(int mask, InfantryGun g)
        {
            return (mask & (1 << (int)g)) != 0;
        }

        internal static int ToggleKitGun(int mask, InfantryGun g)
        {
            int bit = 1 << (int)g;
            if ((mask & bit) != 0)
            {
                int next = mask & ~bit;
                if (next == 0)
                    return mask;
                return next;
            }
            if (CountKit(mask) >= MaxKit)
                return mask;
            return mask | bit;
        }

        internal static int ClampKit(int mask)
        {
            mask &= KitMaskAll;
            if (mask == 0)
                mask = 1;
            int n = CountKit(mask);
            if (n <= MaxKit)
                return mask;
            int keep = 0;
            int have = 0;
            for (int i = 0; i < GunCount && have < MaxKit; i++)
            {
                int bit = 1 << i;
                if ((mask & bit) == 0)
                    continue;
                keep |= bit;
                have++;
            }
            if (keep == 0)
                keep = 1;
            return keep;
        }

        internal static int CountKit(int mask)
        {
            int n = 0;
            for (int i = 0; i < GunCount; i++)
            {
                if ((mask & (1 << i)) != 0)
                    n++;
            }
            return n;
        }

        internal static InfantryGun FirstInMask(int mask)
        {
            for (int i = 0; i < GunCount; i++)
            {
                if ((mask & (1 << i)) != 0)
                    return (InfantryGun)i;
            }
            return InfantryGun.M16;
        }

        internal static string KitSummary(int mask)
        {
            mask &= KitMaskAll;
            if (mask == 0)
                mask = 1;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            int n = 0;
            for (int i = 0; i < GunCount; i++)
            {
                if ((mask & (1 << i)) == 0)
                    continue;
                if (n > 0)
                    sb.Append(", ");
                sb.Append(GunName((InfantryGun)i));
                n++;
            }
            return sb.ToString();
        }

        internal static bool IsAllowed(InfantryGun g)
        {
            int bit = 1 << (int)g;
            return (AllowedMask & bit) != 0;
        }

        internal static void ApplySavedKit()
        {
            int mask = LoadKitMask();
            AllowedMask = ClampKit(mask);
            FullKit = CountKit(AllowedMask) > 1;
            FillAmmo(false);
            Gun = FirstInMask(mask);
            ClearFireState();
        }

        internal static void ApplyCarry(InfantryGun g)
        {
            g = ClampGun((int)g);
            FullKit = false;
            Gun = g;
            AllowedMask = 1 << (int)g;
            FillAmmo(false);
            ClearFireState();
        }

        internal static void ResetLoadout()
        {
            ApplySavedKit();
        }

        internal static void Restock()
        {
            FillAmmo(false);
            ClearFireState();
        }

        internal static void GrantGun(InfantryGun g)
        {
            g = ClampGun((int)g);
            AllowedMask |= 1 << (int)g;
            if (CountKit(AllowedMask) > MaxKit)
            {
                for (int i = 0; i < GunCount; i++)
                {
                    if (i == (int)g)
                        continue;
                    int bit = 1 << i;
                    if ((AllowedMask & bit) == 0)
                        continue;
                    AllowedMask &= ~bit;
                    if (CountKit(AllowedMask) <= MaxKit)
                        break;
                }
            }
            FillAmmo(false);
            Gun = g;
            ClearFireState();
            EnsureView(g);
        }

        internal static void AttachViews(Transform cam)
        {
            EnsureMeshes();
            EnsureMats();
            DestroyViews();
            if (cam == null)
                return;
            _viewRoot = new GameObject("OritasyInfantryViews");
            _viewRoot.transform.SetParent(cam, false);
            _gunGo = new GameObject[GunCount];
            for (int i = 0; i < GunCount; i++)
            {
                if (!IsAllowed((InfantryGun)i))
                    continue;
                _gunGo[i] = MakeViewFor(i);
            }
            ShowGun();
        }

        private static void EnsureView(InfantryGun g)
        {
            EnsureView((int)g);
        }

        private static void EnsureView(int i)
        {
            if (_viewRoot == null)
                return;
            if (_gunGo == null)
                _gunGo = new GameObject[GunCount];
            if (i < 0 || i >= GunCount)
                return;
            if (_gunGo[i] != null)
                return;
            EnsureMeshes();
            EnsureMats();
            _gunGo[i] = MakeViewFor(i);
        }

        private static GameObject MakeViewFor(int i)
        {
            Material fb = _matBlack;
            if (i == 3)
                fb = _matTan;
            else if (i >= 5 && i <= 7)
                fb = _matOlive;
            return MakeView(ViewNames[i], MeshAt(i), MatFor(i, fb), ViewPos[i], ViewLen[i], true);
        }

        internal static void BindToCamera(Camera cam)
        {
            if (_viewRoot == null || cam == null)
                return;
            if (_viewRoot.transform.parent != cam.transform)
                _viewRoot.transform.SetParent(cam.transform, false);
        }

        internal static void DestroyViews()
        {
            InfantryViewAnim.Stop();
            if (_viewRoot != null)
                UnityEngine.Object.Destroy(_viewRoot);
            _viewRoot = null;
            _gunGo = null;
            _shownGun = -99;
        }

        internal static void ClearBolts()
        {
            for (int i = 0; i < Bolts.Count; i++)
            {
                if (Bolts[i].Go != null)
                    UnityEngine.Object.Destroy(Bolts[i].Go);
            }
            Bolts.Clear();
        }

        internal static void Tick(Transform eye, float dt)
        {
            if (eye == null)
                return;
            PollSelect();

            bool adsWant = CanAds(Gun) && Input.GetMouseButton(1) && Gun != InfantryGun.Rbs70;
            Ads = Mathf.MoveTowards(Ads, adsWant ? 1f : 0f, dt * 7f);
            PollZoom(adsWant);
            Recoil = Mathf.MoveTowards(Recoil, 0f, dt * 8f);
            SpreadBloom = Mathf.MoveTowards(SpreadBloom, 0f, dt * (Ads > 0.5f ? 1.6f : 1.05f));
            ShowGun();
            PoseViews(eye, dt);
            CommitReloadIfDue();

            if (Input.GetKeyDown(KeyCode.R))
                TryReload();

            if (Gun == InfantryGun.Rbs70)
                TickRbsLock(eye, dt);
            else
            {
                RbsLock = 0f;
                RbsTarget = null;
            }
            if (Gun == InfantryGun.Sniper)
                TickSniperLock(eye, dt);
            else
            {
                SniperLock = 0f;
                SniperTarget = null;
            }

            if (IsHitscan(Gun))
            {
                if (Gun == InfantryGun.Sniper || Gun == InfantryGun.Shotgun)
                {
                    if (Input.GetMouseButtonDown(0))
                        TryHitscan(eye);
                }
                else if (Input.GetMouseButton(0))
                    TryHitscan(eye);
            }
            else if (Input.GetMouseButtonDown(0))
            {
                if (Gun == InfantryGun.Rbs70)
                    TryRbs(eye);
                else
                    TryRocket(eye);
            }

            TickBolts(dt);
        }

        internal static float FovMul()
        {
            float tight = 0.42f;
            if (Gun == InfantryGun.Sniper)
                tight = Mathf.Lerp(0.16f, 0.038f, Zoom);
            else if (Gun == InfantryGun.G11)
                tight = Mathf.Lerp(0.38f, 0.18f, Zoom);
            else if (Gun == InfantryGun.Pistol)
                tight = Mathf.Lerp(0.72f, 0.42f, Zoom);
            else
                tight = Mathf.Lerp(0.40f, 0.16f, Zoom);
            return Mathf.Lerp(1f, tight, Ads);
        }

        internal static float LookSensMul()
        {
            if (Ads < 0.04f)
                return 1f;
            float z = 1f - Zoom * 0.72f;
            if (z < 0.18f)
                z = 0.18f;
            return Mathf.Lerp(1f, z * 0.42f, Ads);
        }

        internal static float ZoomPower()
        {
            float m = FovMul();
            if (m < 0.02f)
                m = 0.02f;
            return 1f / m;
        }

        private static void PollZoom(bool adsHeld)
        {
            if (!CanAds(Gun) || InfantryHud.BlocksLook)
                return;
            if (!adsHeld && Ads < 0.2f)
                return;
            float wh = Input.GetAxis("Mouse ScrollWheel");
            if (wh > 0.01f)
                Zoom = Mathf.Clamp01(Zoom + 0.11f);
            else if (wh < -0.01f)
                Zoom = Mathf.Clamp01(Zoom - 0.11f);
        }

        internal static string HudLine()
        {
            string n = GunName(Gun);
            int i = (int)Gun;
            string ap = "  " + DmgKind(Gun) + "  PEN " + PenGrade(Gun)
                + "  KE " + Mathf.RoundToInt(Kinetic(Gun));
            if (IsHitscan(Gun))
            {
                string r = Time.time < ReloadUntil ? "  RELOAD" : "";
                string z = Ads > 0.2f
                    ? ("  " + ZoomPower().ToString("0.0") + "x")
                    : "";
                string lk = "";
                if (Gun == InfantryGun.Sniper && SniperTarget != null)
                    lk = "  GUID " + Mathf.RoundToInt(SniperLock * 100f) + "%";
                if (Gun == InfantryGun.G11)
                    lk = UiLang.T("  SMART", "  智能弹");
                return n + "  " + Ammo[i] + "/" + Reserve[i] + ap + z + lk + r;
            }
            if (Gun == InfantryGun.Rbs70)
            {
                string lockS = RbsTarget != null
                    ? ("  LOCK " + Mathf.RoundToInt(RbsLock * 100f) + "%")
                    : "";
                return n + "  " + Ammo[i] + ap + lockS;
            }
            return n + "  " + Ammo[i] + ap;
        }

        internal static int HudSlot()
        {
            int slot = 0;
            for (int i = 0; i < GunCount; i++)
            {
                if (!IsAllowed((InfantryGun)i))
                    continue;
                slot++;
                if (i == (int)Gun)
                    return slot;
            }
            return Mathf.Max(1, (int)Gun + 1);
        }

        internal static string KitHint()
        {
            if (FullKit || CountKit(AllowedMask) > 1)
                return UiLang.T("RMB ADS  wheel zoom  1-4/Q/E  B jet",
                    "右键开镜  滚轮变焦  1-4/Q/E  B买机");
            return UiLang.T("Carry-on only: " + GunName(Gun) + "  PEN " + PenGrade(Gun),
                "登机携带：" + GunName(Gun) + "  穿甲 " + PenGrade(Gun));
        }

        private static void PollSelect()
        {
            if (InfantryShop.MenuOpen || InfantryHud.CatalogOpen || InfantryAirbaseStations.KitOpen)
                return;
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftBracket)
                || Input.GetKeyDown(KeyCode.Comma))
                CycleGun(-1);
            if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.RightBracket)
                || Input.GetKeyDown(KeyCode.Period))
                CycleGun(1);
            if (PressedSlot(1)) TrySelectNth(0);
            if (PressedSlot(2)) TrySelectNth(1);
            if (PressedSlot(3)) TrySelectNth(2);
            if (PressedSlot(4)) TrySelectNth(3);
            if (PressedSlot(5)) TrySelectNth(4);
            if (PressedSlot(6)) TrySelectNth(5);
            if (PressedSlot(7)) TrySelectNth(6);
            if (PressedSlot(8)) TrySelectNth(7);
            if (PressedSlot(9)) TrySelectNth(8);
        }

        private static bool PressedSlot(int n)
        {
            if (n == 1) return Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1);
            if (n == 2) return Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2);
            if (n == 3) return Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3);
            if (n == 4) return Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4);
            if (n == 5) return Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5);
            if (n == 6) return Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6);
            if (n == 7) return Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7);
            if (n == 8) return Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8);
            if (n == 9) return Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9);
            return false;
        }

        private static void TrySelectNth(int index)
        {
            int k = 0;
            for (int i = 0; i < GunCount; i++)
            {
                if (!IsAllowed((InfantryGun)i))
                    continue;
                if (k == index)
                {
                    TrySelect((InfantryGun)i);
                    return;
                }
                k++;
            }
        }

        private static void CycleGun(int dir)
        {
            int cur = (int)Gun;
            for (int n = 0; n < GunCount; n++)
            {
                cur = (cur + dir + GunCount) % GunCount;
                if (IsAllowed((InfantryGun)cur))
                {
                    TrySelect((InfantryGun)cur);
                    return;
                }
            }
        }

        private static void FillAmmo(bool all)
        {
            for (int i = 0; i < GunCount; i++)
            {
                bool on = all || i == (int)Gun || (AllowedMask & (1 << i)) != 0;
                if (!on)
                {
                    Ammo[i] = 0;
                    Reserve[i] = 0;
                    continue;
                }
                switch ((InfantryGun)i)
                {
                    case InfantryGun.M16:
                    case InfantryGun.Ak47:
                        Ammo[i] = 30;
                        Reserve[i] = 90;
                        break;
                    case InfantryGun.Pistol:
                        Ammo[i] = 15;
                        Reserve[i] = 45;
                        break;
                    case InfantryGun.Shotgun:
                        Ammo[i] = 8;
                        Reserve[i] = 24;
                        break;
                    case InfantryGun.Sniper:
                        Ammo[i] = 5;
                        Reserve[i] = 20;
                        break;
                    case InfantryGun.G11:
                        Ammo[i] = 50;
                        Reserve[i] = 100;
                        break;
                    case InfantryGun.At4:
                        Ammo[i] = 6;
                        Reserve[i] = 0;
                        break;
                    case InfantryGun.Rpg7:
                        Ammo[i] = 6;
                        Reserve[i] = 0;
                        break;
                    case InfantryGun.Rbs70:
                        Ammo[i] = 6;
                        Reserve[i] = 0;
                        break;
                    default:
                        Ammo[i] = 0;
                        Reserve[i] = 0;
                        break;
                }
            }
        }

        private static void ClearFireState()
        {
            CancelReload();
            ReloadUntil = 0f;
            NextShotAt = 0f;
            Recoil = 0f;
            SpreadBloom = 0f;
            RecoilRecover = 0f;
            Ads = 0f;
            RbsLock = 0f;
            RbsTarget = null;
            SniperLock = 0f;
            SniperTarget = null;
            Status = "";
        }

        private static bool IsHitscan(InfantryGun g)
        {
            return (int)g <= (int)InfantryGun.Sniper || g == InfantryGun.G11;
        }

        private static bool CanAds(InfantryGun g)
        {
            return g != InfantryGun.At4 && g != InfantryGun.Rpg7 && g != InfantryGun.Rbs70;
        }

        private static void ShowGun()
        {
            if (_gunGo == null)
                return;
            bool hide = InfantryModeService.ThirdPerson
                || (Gun == InfantryGun.Sniper && Ads > 0.45f)
                || (Gun == InfantryGun.G11 && Ads > 0.42f);
            int want = (int)Gun;
            if (want == _shownGun && hide == _shownHide)
                return;
            _shownGun = want;
            _shownHide = hide;
            for (int i = 0; i < _gunGo.Length; i++)
            {
                if (_gunGo[i] != null)
                    _gunGo[i].SetActive(i == want && !hide);
            }
        }

        private static void PoseViews(Transform eye, float dt)
        {
            if (_viewRoot == null)
                return;
            bool sprint = false;
            float speed = 0f;
            float strafe = 0f;
            try
            {
                speed = InfantryModeService.MoveSpeed;
                sprint = InfantryModeService.Sprinting;
                strafe = InfantryModeService.Strafe;
            }
            catch { }
            InfantryViewAnim.TickLoco(dt, speed, sprint, Ads, Time.time < ReloadUntil, strafe);
            Vector3 locoP;
            Vector3 locoE;
            Vector3 animP;
            Vector3 animE;
            InfantryViewAnim.EvaluateLoco(out locoP, out locoE);
            InfantryViewAnim.Evaluate(out animP, out animE);
            Vector3 adsOff = Vector3.Lerp(Vector3.zero, new Vector3(-0.18f, 0.06f, -0.08f), Ads);
            Vector3 kick = new Vector3(0f, 0f, -Recoil * 0.04f);
            Vector3 fitP;
            float fitS;
            ViewFit(out fitP, out fitS);
            _viewRoot.transform.localPosition = kick + adsOff + locoP + animP + fitP;
            _viewRoot.transform.localScale = Vector3.one * fitS;
            _viewRoot.transform.localRotation = Quaternion.Euler(
                -Recoil * 2.2f + locoE.x + animE.x,
                locoE.y + animE.y,
                locoE.z + animE.z);
        }

        /// <summary>
        /// 16:9 / 1080p crops the stock. Wider or taller frames already show the full gun.
        /// </summary>
        private static void ViewFit(out Vector3 pos, out float scale)
        {
            pos = Vector3.zero;
            scale = 1f;
            float h = Screen.height;
            if (h < 1f)
                h = 1f;
            float aspect = Screen.width / h;
            float squeezeA = (1.95f - aspect) / 0.55f;
            if (squeezeA < 0f) squeezeA = 0f;
            if (squeezeA > 1f) squeezeA = 1f;
            float squeezeH = (1240f - h) / 280f;
            if (squeezeH < 0f) squeezeH = 0f;
            if (squeezeH > 1f) squeezeH = 1f;
            float squeeze = squeezeA * 0.62f + squeezeH * 0.48f;
            if (squeeze > 1f)
                squeeze = 1f;
            float adsKeep = 1f - Ads * 0.8f;
            if (adsKeep < 0.2f)
                adsKeep = 0.2f;
            squeeze *= adsKeep;
            scale = 1f - squeeze * 0.28f;
            pos = new Vector3(-0.085f * squeeze, 0.05f * squeeze, 0.09f * squeeze);
        }

        private static void TryReload()
        {
            if (!IsHitscan(Gun))
                return;
            int i = (int)Gun;
            int mag = MagSize(Gun);
            if (Time.time < ReloadUntil || Ammo[i] >= mag || Reserve[i] <= 0)
                return;
            int need = mag - Ammo[i];
            int take = need <= Reserve[i] ? need : Reserve[i];
            if (take <= 0)
                return;
            _pendingReloadGun = i;
            _pendingReloadTake = take;
            InfantryViewAnim.PlayReload(i);
            float wait = ReloadTime(Gun);
            float clip = InfantryViewAnim.Duration(i, InfantryViewAnim.KindReload);
            if (clip > wait)
                wait = clip;
            ReloadUntil = Time.time + wait;
            Status = "reload";
        }

        private static void CommitReloadIfDue()
        {
            if (_pendingReloadGun < 0)
                return;
            if (Time.time < ReloadUntil)
                return;
            int i = _pendingReloadGun;
            int take = _pendingReloadTake;
            _pendingReloadGun = -1;
            _pendingReloadTake = 0;
            InfantryViewAnim.Stop();
            if (i < 0 || i >= GunCount || take <= 0)
                return;
            if (take > Reserve[i])
                take = Reserve[i];
            Reserve[i] -= take;
            int mag = MagSize((InfantryGun)i);
            int room = mag - Ammo[i];
            if (take > room)
                take = room;
            if (take < 0)
                take = 0;
            Ammo[i] += take;
            Status = "";
        }

        private static void CancelReload()
        {
            _pendingReloadGun = -1;
            _pendingReloadTake = 0;
            ReloadUntil = 0f;
            InfantryViewAnim.Stop();
        }

        private static int MagSize(InfantryGun g)
        {
            if (g == InfantryGun.Pistol)
                return 15;
            if (g == InfantryGun.Shotgun)
                return 8;
            if (g == InfantryGun.Sniper)
                return 5;
            if (g == InfantryGun.G11)
                return 50;
            if (g == InfantryGun.At4 || g == InfantryGun.Rpg7 || g == InfantryGun.Rbs70)
                return 6;
            return 30;
        }

        private static float ReloadTime(InfantryGun g)
        {
            if (g == InfantryGun.Shotgun)
                return 3.2f;
            if (g == InfantryGun.Sniper)
                return 2.8f;
            if (g == InfantryGun.Pistol)
                return 1.6f;
            return 2.4f;
        }

        private static float Rpm(InfantryGun g)
        {
            if (g == InfantryGun.Ak47)
                return 600f;
            if (g == InfantryGun.Pistol)
                return 380f;
            if (g == InfantryGun.Shotgun)
                return 70f;
            if (g == InfantryGun.Sniper)
                return 48f;
            if (g == InfantryGun.G11)
                return 460f;
            return 700f;
        }

        private static float HitDamage(InfantryGun g)
        {
            if (g == InfantryGun.Ak47)
                return 48f;
            if (g == InfantryGun.Pistol)
                return 28f;
            if (g == InfantryGun.Shotgun)
                return 18f;
            if (g == InfantryGun.Sniper)
                return 110f;
            if (g == InfantryGun.G11)
                return 36f;
            return 42f;
        }

        private static float HitRange(InfantryGun g)
        {
            if (g == InfantryGun.Pistol)
                return 900f;
            if (g == InfantryGun.Shotgun)
                return 260f;
            if (g == InfantryGun.Sniper)
                return 5200f;
            if (g == InfantryGun.Ak47)
                return 3200f;
            if (g == InfantryGun.G11)
                return 2800f;
            return 3800f;
        }

        private static float BulletSpeed(InfantryGun g)
        {
            if (g == InfantryGun.Pistol)
                return 480f;
            if (g == InfantryGun.Shotgun)
                return 500f;
            if (g == InfantryGun.Sniper)
                return 1180f;
            if (g == InfantryGun.Ak47)
                return 860f;
            if (g == InfantryGun.G11)
                return 720f;
            return 1020f;
        }

        private static void TryHitscan(Transform eye)
        {
            if (Time.time < ReloadUntil)
                return;
            int i = (int)Gun;
            if (Ammo[i] <= 0)
            {
                TryReload();
                return;
            }
            float gap = 60f / Rpm(Gun);
            if (Time.time < NextShotAt)
                return;
            NextShotAt = Time.time + gap;
            Ammo[i]--;
            float bloomAdd = 0.12f;
            if (Gun == InfantryGun.Ak47) bloomAdd = 0.16f;
            else if (Gun == InfantryGun.M16) bloomAdd = 0.13f;
            else if (Gun == InfantryGun.Pistol) bloomAdd = 0.22f;
            else if (Gun == InfantryGun.Shotgun) bloomAdd = 0.08f;
            else if (Gun == InfantryGun.Sniper) bloomAdd = 0.55f;
            else if (Gun == InfantryGun.G11) bloomAdd = 0.06f;
            SpreadBloom = Mathf.Min(1f, SpreadBloom + bloomAdd);
            Recoil = Mathf.Min(1f, Recoil + (Gun == InfantryGun.Sniper ? 0.85f : 0.38f));
            float kickUp = 0.42f + SpreadBloom * 1.15f;
            if (Gun == InfantryGun.Sniper) kickUp = 1.55f;
            else if (Gun == InfantryGun.Shotgun) kickUp = 1.05f;
            else if (Gun == InfantryGun.Ak47) kickUp += 0.18f;
            if (Ads > 0.5f)
                kickUp *= 0.42f;
            float kickYaw = (UnityEngine.Random.value - 0.42f) * (0.35f + SpreadBloom * 0.85f);
            InfantryModeService.NudgeLook(kickYaw, -kickUp);
            RecoilRecover += kickUp * 0.58f;
            InfantryWarnService.NoteFireHeat();
            InfantryViewAnim.PlayFire(i);
            int pellets = Gun == InfantryGun.Shotgun ? 8 : 1;
            bool tap = Input.GetMouseButtonDown(0);
            float spread = ShotSpread(Gun);
            if (tap && Gun != InfantryGun.Shotgun)
                spread = 0f;
            Vector3 muzzle = MuzzlePos(eye);
            InfantryFx.Muzzle(muzzle, eye.forward);
            float range = HitRange(Gun);
            float speed = BulletSpeed(Gun);
            for (int p = 0; p < pellets; p++)
            {
                Vector3 dir = eye.forward;
                if (spread > 0.0005f)
                {
                    dir += eye.right * (UnityEngine.Random.value - 0.5f) * 2f * spread;
                    dir += eye.up * (UnityEngine.Random.value - 0.5f) * 2f * spread;
                    dir.Normalize();
                }
                SpawnSlug(muzzle + dir * 0.15f, dir, speed, range, Gun, null);
            }
        }

        internal static void ApplyRecoilRecover(ref float yaw, ref float pitch, float dt)
        {
            if (RecoilRecover <= 0.0001f)
                return;
            float rec = RecoilRecover * dt * 9.5f;
            if (rec > RecoilRecover)
                rec = RecoilRecover;
            RecoilRecover -= rec;
            pitch += rec;
            if (pitch > 85f) pitch = 85f;
            if (pitch < -85f) pitch = -85f;
        }

        private static float ShotSpread(InfantryGun g)
        {
            float s = 0.0045f;
            if (g == InfantryGun.Ak47) s = 0.0075f;
            else if (g == InfantryGun.M16) s = 0.0055f;
            else if (g == InfantryGun.Pistol) s = 0.014f;
            else if (g == InfantryGun.Shotgun) s = 0.058f;
            else if (g == InfantryGun.Sniper) s = 0.0018f;
            else if (g == InfantryGun.G11) s = 0.0032f;
            s += SpreadBloom * (g == InfantryGun.Ak47 ? 0.028f : 0.022f);
            if (Ads > 0.5f)
                s *= 0.32f;
            else if (Ads > 0.15f)
                s *= 0.62f;
            if (InfantryModeService.Sprinting)
                s *= 1.85f;
            else if (InfantryModeService.MoveSpeed > 1.2f)
                s *= 1.28f;
            return s;
        }

        private static void SpawnSlug(Vector3 pos, Vector3 dir, float speed, float range, InfantryGun g, Transform ignore)
        {
            if (dir.sqrMagnitude < 0.01f)
                dir = Vector3.forward;
            dir.Normalize();
            GameObject go = new GameObject("OritasySlug");
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(dir);
            _tracerN++;
            bool bright = g == InfantryGun.Sniper
                || g == InfantryGun.Shotgun
                || (_tracerN % 4) == 0;
            float len = g == InfantryGun.Sniper ? 3.2f : (g == InfantryGun.Shotgun ? 1.2f : (bright ? 2.4f : 1.35f));
            Color col = TracerColor(g);
            if (g == InfantryGun.Sniper)
                col = new Color(0.35f, 0.95f, 1f, 1f);
            else if (g == InfantryGun.G11)
                col = new Color(1f, 0.12f, 0.06f, 1f);
            else if (bright)
                col = new Color(1f, 0.42f, 0.12f, 1f);
            if (g == InfantryGun.G11)
                InfantryFx.DressG11Dart(go, col);
            else
                InfantryFx.DressSlug(go, col, len, bright);
            Bolt b = new Bolt();
            b.Go = go;
            b.Vel = dir * speed;
            b.DieAt = Time.time + Mathf.Max(0.35f, range / speed);
            b.Homing = false;
            b.GuideLook = false;
            b.Target = null;
            b.SeekXf = null;
            b.Kind = 10 + (int)g;
            b.Shot = g;
            b.Ignore = ignore;
            if (g == InfantryGun.G11)
            {
                b.Path = new List<Vector3>(48);
                b.Path.Add(pos);
            }
            if (g == InfantryGun.Sniper)
            {
                if (SniperTarget != null && SniperLock >= 0.82f)
                {
                    b.Homing = true;
                    b.Target = SniperTarget;
                    b.DieAt = Time.time + 6.5f;
                }
                else
                {
                    b.GuideLook = true;
                    b.GuideUntil = Time.time + 1.15f;
                }
            }
            else if (g == InfantryGun.G11)
            {
                b.Homing = true;
                b.DieAt = Time.time + 7.5f;
                AcquireSmart(b);
            }
            Bolts.Add(b);
        }

        private static void TryRocket(Transform eye)
        {
            int i = (int)Gun;
            if (Ammo[i] <= 0)
            {
                Status = "empty";
                return;
            }
            if (Time.time < NextShotAt)
                return;
            Ammo[i]--;
            NextShotAt = Time.time + 1.2f;
            Recoil = 1f;
            InfantryViewAnim.PlayFire(i);
            bool rpg = Gun == InfantryGun.Rpg7;
            float speed = rpg ? 160f : 220f;
            float life = rpg ? 10f : 8f;
            float rad = rpg ? 0.14f : 0.12f;
            InfantryFx.Muzzle(MuzzlePos(eye), eye.forward);
            SpawnBolt(eye, speed, life, false, null, rpg ? 1 : 0, rad);
            Status = rpg ? "RPG-7" : "AT-4";
        }

        private static void TryRbs(Transform eye)
        {
            int i = (int)InfantryGun.Rbs70;
            if (Ammo[i] <= 0)
            {
                Status = "RBS 70 empty";
                return;
            }
            if (RbsTarget == null || RbsLock < 0.99f)
            {
                Status = "no lock";
                return;
            }
            if (Time.time < NextShotAt)
                return;
            Ammo[i]--;
            NextShotAt = Time.time + 1.6f;
            Recoil = 0.7f;
            InfantryViewAnim.PlayFire(i);
            InfantryFx.Muzzle(MuzzlePos(eye), eye.forward);
            SpawnBolt(eye, 380f, 14f, true, RbsTarget, 2, 0.1f);
            RbsLock = 0f;
            Status = "RBS 70 fire";
        }

        private static void TickRbsLock(Transform eye, float dt)
        {
            if (Time.unscaledTime >= _aimScanAt)
            {
                _aimScanAt = Time.unscaledTime + 0.1f;
                Unit bestScan = null;
                float bestAng = 9f;
                try
                {
                    List<Unit> all = UnitRegistry.allUnits;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                        {
                            Unit u = all[i];
                            if (u == null || u.disabled)
                                continue;
                            if (!(u is Aircraft))
                                continue;
                            Vector3 to = u.transform.position - eye.position;
                            float dist = to.magnitude;
                            if (dist < 80f || dist > 8000f)
                                continue;
                            float ang = Vector3.Angle(eye.forward, to);
                            if (ang < bestAng)
                            {
                                bestAng = ang;
                                bestScan = u;
                            }
                        }
                    }
                }
                catch { }
                _rbsScan = bestScan;
            }
            Unit best = _rbsScan;

            if (best == null)
            {
                RbsLock = Mathf.MoveTowards(RbsLock, 0f, dt * 0.6f);
                RbsTarget = null;
                return;
            }
            if (!object.ReferenceEquals(best, RbsTarget))
            {
                RbsTarget = best;
                RbsLock = 0f;
            }
            bool hold = Input.GetMouseButton(1);
            if (hold)
                RbsLock = Mathf.MoveTowards(RbsLock, 1f, dt / 1.15f);
            else
                RbsLock = Mathf.MoveTowards(RbsLock, 0f, dt * 0.35f);
        }

        private static void TickSniperLock(Transform eye, float dt)
        {
            float cone = Mathf.Lerp(4.2f, 1.4f, Zoom);
            if (Time.unscaledTime >= _aimScanAt)
            {
                _aimScanAt = Time.unscaledTime + 0.1f;
                _snpScan = FindAimUnit(eye, 40f, 9000f, cone, true);
            }
            Unit best = _snpScan;
            if (best == null)
            {
                SniperLock = Mathf.MoveTowards(SniperLock, 0f, dt * 0.55f);
                if (SniperLock < 0.08f)
                    SniperTarget = null;
                return;
            }
            if (!object.ReferenceEquals(best, SniperTarget))
            {
                SniperTarget = best;
                SniperLock = 0f;
            }
            if (Ads > 0.35f)
                SniperLock = Mathf.MoveTowards(SniperLock, 1f, dt / 0.85f);
            else
                SniperLock = Mathf.MoveTowards(SniperLock, 0f, dt * 0.4f);
        }

        private static Unit FindAimUnit(Transform eye, float minDist, float maxDist, float maxAng, bool vehicles)
        {
            if (eye == null)
                return null;
            Unit best = null;
            float bestAng = maxAng;
            try
            {
                List<Unit> all = UnitRegistry.allUnits;
                if (all == null)
                    return null;
                for (int i = 0; i < all.Count; i++)
                {
                    Unit u = all[i];
                    if (u == null || u.disabled)
                        continue;
                    bool ok = u is Aircraft;
                    if (vehicles && (u is GroundVehicle || u is Ship))
                        ok = true;
                    if (!ok)
                        continue;
                    Vector3 to = u.transform.position - eye.position;
                    float dist = to.magnitude;
                    if (dist < minDist || dist > maxDist)
                        continue;
                    float ang = Vector3.Angle(eye.forward, to);
                    if (ang < bestAng)
                    {
                        bestAng = ang;
                        best = u;
                    }
                }
            }
            catch { }
            return best;
        }

        private static void AcquireSmart(Bolt b)
        {
            if (b == null || b.Go == null)
                return;
            if (b.Target != null && !b.Target.disabled)
                return;
            if (b.SeekXf != null)
            {
                InfantryAiPawn keep = null;
                try { keep = b.SeekXf.GetComponent<InfantryAiPawn>(); }
                catch { }
                if (keep != null && keep.Hp > 0f)
                    return;
                b.SeekXf = null;
            }
            Vector3 pos = b.Go.transform.position;
            Vector3 fwd = b.Vel.sqrMagnitude > 0.01f ? b.Vel.normalized : b.Go.transform.forward;
            Unit u = FindSeekUnit(pos, fwd, 22f, 520f);
            if (u == null)
                u = FindSeekUnit(pos, fwd, 42f, 420f);
            if (u != null)
            {
                b.Target = u;
                return;
            }
            Transform inf = FindSeekInfantry(pos, fwd, 28f, 380f);
            if (inf == null)
                inf = FindSeekInfantry(pos, fwd, 48f, 280f);
            b.SeekXf = inf;
        }

        private static Unit FindSeekUnit(Vector3 pos, Vector3 fwd, float maxAng, float maxDist)
        {
            Unit best = null;
            float bestScore = float.MaxValue;
            FactionHQ hq = LocalHq();
            try
            {
                List<Unit> all = UnitRegistry.allUnits;
                if (all == null)
                    return null;
                for (int i = 0; i < all.Count; i++)
                {
                    Unit u = all[i];
                    if (u == null || u.disabled)
                        continue;
                    if (!(u is Aircraft) && !(u is GroundVehicle) && !(u is Ship))
                        continue;
                    if (IsFriendlyUnit(u, hq))
                        continue;
                    Vector3 to = u.transform.position - pos;
                    float dist = to.magnitude;
                    if (dist < 4f || dist > maxDist)
                        continue;
                    if (Vector3.Angle(fwd, to) > maxAng)
                        continue;
                    float score = dist + Vector3.Angle(fwd, to) * 4f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = u;
                    }
                }
            }
            catch { }
            return best;
        }

        private static Transform FindSeekInfantry(Vector3 pos, Vector3 fwd, float maxAng, float maxDist)
        {
            Transform best = null;
            float bestScore = float.MaxValue;
            int n = InfantryAi.Count;
            for (int i = 0; i < n; i++)
            {
                InfantryAiPawn pawn = InfantryAi.BotAt(i);
                if (pawn == null || pawn.Hp <= 0f || pawn.Friendly)
                    continue;
                Vector3 to = pawn.transform.position + Vector3.up * 1.05f - pos;
                float dist = to.magnitude;
                if (dist < 2f || dist > maxDist)
                    continue;
                if (Vector3.Angle(fwd, to) > maxAng)
                    continue;
                float score = dist + Vector3.Angle(fwd, to) * 3f;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = pawn.transform;
                }
            }
            return best;
        }

        private static FactionHQ LocalHq()
        {
            FactionHQ hq = null;
            try { GameManager.GetLocalHQ(out hq); }
            catch { }
            return hq;
        }

        private static bool IsFriendlyUnit(Unit u, FactionHQ local)
        {
            if (u == null || local == null)
                return false;
            try
            {
                FactionHQ other = u.NetworkHQ;
                if (other == null)
                    return false;
                return object.ReferenceEquals(other, local);
            }
            catch
            {
                return false;
            }
        }

        private static void HurtSeekXf(Transform xf, float pierce, float blast, float impact, Vector3 incoming, InfantryGun g)
        {
            if (xf == null)
                return;
            ScaleVsTarget(ref pierce, ref blast, ref impact, g, xf);
            ModularArmorService.RememberInfantryShot(PenGrade(g), incoming, IsPureKe(g));
            PersistentID dealer = LocalDealerId();
            IDamageable dmg = null;
            try { dmg = xf.GetComponentInParent<IDamageable>(); }
            catch { }
            if (dmg == null)
                return;
            try { dmg.TakeDamage(pierce, blast, 1f, 0f, impact, dealer); }
            catch
            {
                try { dmg.ApplyDamage(pierce, blast, 0f, impact); }
                catch { }
            }
        }

        internal static void AiFire(Vector3 origin, Vector3 dir, InfantryGun g, Transform ignore)
        {
            if (dir.sqrMagnitude < 0.01f)
                return;
            dir.Normalize();
            float spread = 0.012f;
            dir += Vector3.Cross(dir, Vector3.up).normalized * (UnityEngine.Random.value - 0.5f) * 2f * spread;
            dir += Vector3.up * (UnityEngine.Random.value - 0.5f) * 2f * spread;
            dir.Normalize();
            InfantryFx.Muzzle(origin);
            SpawnSlug(origin + dir * 0.12f, dir, BulletSpeed(g), HitRange(g), g, ignore);
        }

        private static void SpawnBolt(Transform eye, float speed, float life, bool homing, Unit tgt, int kind, float radius)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = kind == 2 ? "OritasyRbsBolt" : (kind == 1 ? "OritasyRpg" : "OritasyAt4Rocket");
            go.transform.position = eye.position + eye.forward * 1.1f;
            go.transform.rotation = Quaternion.LookRotation(eye.forward) * Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(radius, radius * 4f, radius);
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                UnityEngine.Object.Destroy(col);
            Color boltC = kind == 2
                ? new Color(0.45f, 0.95f, 1f)
                : (kind == 1 ? new Color(1f, 0.45f, 0.12f) : new Color(0.85f, 0.75f, 0.35f));
            InfantryFx.DressBolt(go, boltC);
            Bolt b = new Bolt();
            b.Go = go;
            b.Vel = eye.forward * speed;
            b.DieAt = Time.time + life;
            b.Homing = homing;
            b.Target = tgt;
            b.Kind = kind;
            Bolts.Add(b);
        }

        private static void NoteG11Path(Bolt b, Vector3 p)
        {
            if (b == null || b.Shot != InfantryGun.G11 || b.Path == null)
                return;
            if (b.Path.Count > 0)
            {
                Vector3 last = b.Path[b.Path.Count - 1];
                if ((p - last).sqrMagnitude < 0.16f)
                    return;
            }
            if (b.Path.Count >= 80)
                b.Path.RemoveAt(0);
            b.Path.Add(p);
        }

        private static void FinishG11(Bolt b)
        {
            if (b == null || b.Shot != InfantryGun.G11)
                return;
            if (b.Go != null)
                NoteG11Path(b, b.Go.transform.position);
            InfantryFx.ShowG11Path(b.Path);
        }

        private static void TickBolts(float dt)
        {
            for (int i = Bolts.Count - 1; i >= 0; i--)
            {
                Bolt b = Bolts[i];
                if (b.Go == null || Time.time >= b.DieAt)
                {
                    FinishG11(b);
                    if (b.Go != null)
                        UnityEngine.Object.Destroy(b.Go);
                    Bolts.RemoveAt(i);
                    continue;
                }
                if (b.Shot == InfantryGun.G11)
                    AcquireSmart(b);
                Vector3 wantDir = Vector3.zero;
                bool steer = false;
                if (b.Homing && b.Target != null && !b.Target.disabled)
                {
                    wantDir = (b.Target.transform.position - b.Go.transform.position);
                    steer = wantDir.sqrMagnitude > 0.01f;
                }
                else if (b.SeekXf != null)
                {
                    wantDir = (b.SeekXf.position + Vector3.up * 1.05f - b.Go.transform.position);
                    steer = wantDir.sqrMagnitude > 0.01f;
                }
                if (steer)
                {
                    float turn = b.Shot == InfantryGun.G11 ? 6.4f : (b.Kind >= 10 ? 2.6f : 1.8f);
                    b.Vel = Vector3.RotateTowards(b.Vel, wantDir.normalized * b.Vel.magnitude, turn * dt, 220f * dt);
                }
                else if (b.GuideLook && Time.time < b.GuideUntil)
                {
                    Vector3 look = InfantryModeService.EyeFwd;
                    if (look.sqrMagnitude > 0.01f)
                        b.Vel = Vector3.RotateTowards(b.Vel, look.normalized * b.Vel.magnitude, 2.1f * dt, 110f * dt);
                }
                Vector3 pos = b.Go.transform.position;
                Vector3 next = pos + b.Vel * dt;
                Vector3 delta = next - pos;
                float dist = delta.magnitude;
                RaycastHit hit;
                if (dist > 0.01f && WeaponRay(pos, delta / dist, dist + 0.55f, out hit, b.Ignore))
                {
                    if (b.Kind >= 10)
                    {
                        InfantryFx.Sparks(hit.point, hit.normal);
                        HurtPoint(hit, PierceHp(b.Shot), BlastHp(b.Shot), 0f, Kinetic(b.Shot),
                            b.Vel.sqrMagnitude > 0.01f ? b.Vel.normalized : Vector3.forward, b.Shot);
                        NoteG11Path(b, hit.point);
                        FinishG11(b);
                        if (b.Go != null)
                            UnityEngine.Object.Destroy(b.Go);
                        Bolts.RemoveAt(i);
                    }
                    else
                        Detonate(b, hit.point);
                    continue;
                }
                if (b.Homing && b.Target != null)
                {
                    float near = b.Kind >= 10 ? 2.8f : 14f;
                    if (b.Shot == InfantryGun.G11)
                        near = 1.8f;
                    if (Vector3.Distance(next, b.Target.transform.position) < near)
                    {
                        if (b.Kind >= 10)
                        {
                            InfantryFx.Sparks(next, -b.Vel.normalized);
                            HurtUnit(b.Target, PierceHp(b.Shot), BlastHp(b.Shot), 0f, Kinetic(b.Shot),
                                b.Vel.sqrMagnitude > 0.01f ? b.Vel.normalized : Vector3.forward,
                                b.Shot, false);
                            NoteG11Path(b, next);
                            FinishG11(b);
                            if (b.Go != null)
                                UnityEngine.Object.Destroy(b.Go);
                            Bolts.RemoveAt(i);
                        }
                        else
                            Detonate(b, next);
                        continue;
                    }
                }
                else if (b.SeekXf != null)
                {
                    Vector3 aim = b.SeekXf.position + Vector3.up * 1.05f;
                    if (Vector3.Distance(next, aim) < 1.45f)
                    {
                        InfantryFx.Sparks(next, -b.Vel.normalized);
                        HurtSeekXf(b.SeekXf, PierceHp(b.Shot), BlastHp(b.Shot), Kinetic(b.Shot),
                            b.Vel.sqrMagnitude > 0.01f ? b.Vel.normalized : Vector3.forward, b.Shot);
                        NoteG11Path(b, next);
                        FinishG11(b);
                        if (b.Go != null)
                            UnityEngine.Object.Destroy(b.Go);
                        Bolts.RemoveAt(i);
                        continue;
                    }
                }
                b.Go.transform.position = next;
                NoteG11Path(b, next);
                if (b.Vel.sqrMagnitude > 1f)
                {
                    Quaternion look = Quaternion.LookRotation(b.Vel.normalized);
                    if (b.Kind >= 10)
                        b.Go.transform.rotation = look;
                    else
                        b.Go.transform.rotation = look * Quaternion.Euler(90f, 0f, 0f);
                }
            }
        }

        private static void Detonate(Bolt b, Vector3 at)
        {
            InfantryGun g = InfantryGun.At4;
            if (b.Kind == 1)
                g = InfantryGun.Rpg7;
            else if (b.Kind == 2)
                g = InfantryGun.Rbs70;
            float blast = g == InfantryGun.Rpg7 ? 22f : (g == InfantryGun.Rbs70 ? 8f : 16f);
            Vector3 dir = b.Vel.sqrMagnitude > 0.01f ? b.Vel.normalized : Vector3.forward;
            try
            {
                List<Unit> all = UnitRegistry.allUnits;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Unit u = all[i];
                        if (u == null || u.disabled)
                            continue;
                        float d = Vector3.Distance(u.transform.position, at);
                        if (d > blast)
                            continue;
                        float fall = 1f - d / blast;
                        HurtUnit(u, PierceHp(g) * fall, BlastHp(g) * fall, 0f, Kinetic(g) * fall, dir, g, false);
                    }
                }
            }
            catch { }
            InfantryFx.Blast(at);
            if (b.Go != null)
                UnityEngine.Object.Destroy(b.Go);
            Bolts.Remove(b);
        }

        private static void ScaleVsTarget(ref float pierce, ref float blast, ref float impact, InfantryGun g, Transform hitXf)
        {
            if (hitXf == null)
                return;
            bool inf = false;
            bool air = false;
            bool building = false;
            bool vehicle = false;
            try
            {
                if (hitXf.GetComponentInParent<InfantryPawn>() != null
                    || hitXf.GetComponentInParent<InfantryAiPawn>() != null
                    || hitXf.GetComponentInParent<PilotDismounted>() != null)
                    inf = true;
                else if (hitXf.GetComponentInParent<Aircraft>() != null)
                    air = true;
                else if (hitXf.GetComponentInParent<Building>() != null
                    || hitXf.GetComponentInParent<MapBuilding>() != null)
                    building = true;
                else if (hitXf.GetComponentInParent<GroundVehicle>() != null
                    || hitXf.GetComponentInParent<Ship>() != null)
                    vehicle = true;
            }
            catch { }
            float mul = 1f;
            if (IsPureKe(g))
            {
                if (inf)
                    mul = 1f;
                else if (building)
                    mul = 1f;
                else if (vehicle)
                    mul = 0.03f;
                else if (air)
                    mul = 0.015f;
            }
            else if (g == InfantryGun.Rbs70)
            {
                if (building)
                    mul = 0.22f;
                else if (vehicle)
                    mul = 0.35f;
                else if (air)
                    mul = 0.8f;
            }
            else
            {
                if (building)
                    mul = 0.38f;
                else if (vehicle)
                    mul = 0.62f;
                else if (air)
                    mul = 0.4f;
            }
            pierce *= mul;
            blast *= mul;
            impact *= mul;
        }

        private static void HurtPoint(RaycastHit hit, float pierce, float blast, float fire, float impact, Vector3 incoming, InfantryGun g)
        {
            Transform xf = null;
            try { xf = hit.collider != null ? hit.collider.transform : null; }
            catch { }
            ScaleVsTarget(ref pierce, ref blast, ref impact, g, xf);
            ModularArmorService.RememberInfantryShot(PenGrade(g), incoming, IsPureKe(g));
            PersistentID dealer = LocalDealerId();
            UnitPart part = null;
            try { part = hit.collider.GetComponentInParent<UnitPart>(); }
            catch { }
            if (part != null)
            {
                try { part.TakeDamage(pierce, blast, 1f, fire, impact, dealer); }
                catch { }
                return;
            }
            IDamageable dmg = null;
            try { dmg = hit.collider.GetComponentInParent<IDamageable>(); }
            catch { }
            if (dmg != null)
            {
                try { dmg.TakeDamage(pierce, blast, 1f, fire, impact, dealer); }
                catch
                {
                    try { dmg.ApplyDamage(pierce, blast, fire, impact); }
                    catch { }
                }
                return;
            }
            Unit u = null;
            try { u = hit.collider.GetComponentInParent<Unit>(); }
            catch { }
            if (u != null)
                HurtUnit(u, pierce, blast, fire, impact, incoming, g, true);
        }

        private static void HurtUnit(Unit u, float pierce, float blast, float fire, float impact, Vector3 incoming, InfantryGun g, bool alreadyScaled)
        {
            if (u == null)
                return;
            if (!alreadyScaled)
            {
                Transform xf = null;
                try { xf = u.transform; }
                catch { }
                ScaleVsTarget(ref pierce, ref blast, ref impact, g, xf);
            }
            ModularArmorService.RememberInfantryShot(PenGrade(g), incoming, IsPureKe(g));
            PersistentID dealer = LocalDealerId();
            UnitPart[] parts = null;
            try { parts = u.GetComponentsInChildren<UnitPart>(); }
            catch { }
            if (parts != null && parts.Length > 0 && parts[0] != null)
            {
                try { parts[0].TakeDamage(pierce, blast, 1f, fire, impact, dealer); }
                catch { }
                return;
            }
            IDamageable d = u as IDamageable;
            if (d != null)
            {
                try { d.TakeDamage(pierce, blast, 1f, fire, impact, dealer); }
                catch
                {
                    try { d.ApplyDamage(pierce, blast, fire, impact); }
                    catch { }
                }
            }
        }

        private static PersistentID LocalDealerId()
        {
            Aircraft ac;
            try
            {
                if (GameManager.GetLocalAircraft(out ac) && ac != null)
                    return ac.persistentID;
            }
            catch { }
            return PersistentID.None;
        }

        private static Mesh MeshAt(int i)
        {
            if (_meshes == null || i < 0 || i >= _meshes.Length)
                return null;
            return _meshes[i];
        }

        private static Vector3 MuzzlePos(Transform eye)
        {
            Vector3 hip = eye.position + eye.forward * 0.40f + eye.right * 0.16f + eye.up * -0.10f;
            Vector3 ads = eye.position + eye.forward * 0.42f + eye.right * 0.02f + eye.up * -0.02f;
            return Vector3.Lerp(hip, ads, Ads);
        }

        private static Color TracerColor(InfantryGun g)
        {
            if (g == InfantryGun.Sniper)
                return new Color(1f, 0.35f, 0.2f, 1f);
            if (g == InfantryGun.G11)
                return new Color(1f, 0.1f, 0.05f, 1f);
            if (g == InfantryGun.Shotgun)
                return new Color(1f, 0.82f, 0.35f, 1f);
            return new Color(1f, 0.92f, 0.45f, 1f);
        }

        private static bool WeaponRay(Vector3 origin, Vector3 dir, float range, out RaycastHit hit)
        {
            return WeaponRay(origin, dir, range, out hit, null);
        }

        private static bool WeaponRay(Vector3 origin, Vector3 dir, float range, out RaycastHit hit, Transform extraIgnore)
        {
            hit = default(RaycastHit);
            if (range < 0.05f)
                return false;
            int n = 0;
            try
            {
                n = Physics.RaycastNonAlloc(origin, dir, RayBuf, range,
                    InfantryPerf.SightMask, QueryTriggerInteraction.Ignore);
            }
            catch
            {
                return false;
            }
            if (n <= 0)
                return false;
            Transform pawn = InfantryModeService.PawnRoot;
            float best = float.MaxValue;
            bool ok = false;
            int cap = n < RayBuf.Length ? n : RayBuf.Length;
            for (int i = 0; i < cap; i++)
            {
                Collider col = RayBuf[i].collider;
                if (col == null)
                    continue;
                Transform t = col.transform;
                if (pawn != null && (t == pawn || t.IsChildOf(pawn)))
                    continue;
                if (extraIgnore != null && (t == extraIgnore || t.IsChildOf(extraIgnore)))
                    continue;
                if (RayBuf[i].distance < 1.15f)
                {
                    Unit near = null;
                    try { near = col.GetComponentInParent<Unit>(); }
                    catch { }
                    if (near == null)
                    {
                        IDamageable dmg = null;
                        try { dmg = col.GetComponentInParent<IDamageable>(); }
                        catch { }
                        if (dmg == null)
                            continue;
                    }
                }
                if (RayBuf[i].distance < best)
                {
                    best = RayBuf[i].distance;
                    hit = RayBuf[i];
                    ok = true;
                }
            }
            return ok;
        }

        private static GameObject MakeView(string name, Mesh mesh, Material mat, Vector3 local, float wantLen, bool yaw180)
        {
            GameObject go;
            if (mesh != null)
            {
                go = new GameObject(name);
                MeshFilter mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                float sc = FitMeshScale(mesh, wantLen);
                go.transform.localScale = Vector3.one * sc;
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                Collider c = go.GetComponent<Collider>();
                if (c != null)
                    UnityEngine.Object.Destroy(c);
                go.transform.localScale = new Vector3(0.05f, 0.05f, wantLen > 0.1f ? wantLen : 0.45f);
                Renderer r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                yaw180 = false;
            }
            go.transform.SetParent(_viewRoot.transform, false);
            go.transform.localPosition = local;
            go.transform.localRotation = yaw180 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            return go;
        }

        private static float FitMeshScale(Mesh mesh, float wantLen)
        {
            if (mesh == null || wantLen < 0.05f)
                return 1f;
            Vector3 sz = mesh.bounds.size;
            float longest = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
            if (longest < 0.01f)
                return 1f;
            return wantLen / longest;
        }

        private static void EnsureMats()
        {
            if (_matOlive != null)
                return;
            Material src = InfantryFx.UnlitMat();
            if (src == null)
            {
                Shader sh = Shader.Find("Unlit/Color");
                if (sh == null)
                    sh = Shader.Find("Standard");
                if (sh == null)
                    sh = Shader.Find("Diffuse");
                src = new Material(sh);
            }
            _matOlive = new Material(src);
            _matOlive.color = new Color(0.42f, 0.50f, 0.30f);
            _matBlack = new Material(src);
            _matBlack.color = new Color(0.16f, 0.16f, 0.17f);
            _matGrey = new Material(src);
            _matGrey.color = new Color(0.55f, 0.48f, 0.40f);
            _matTan = new Material(src);
            _matTan.color = new Color(0.55f, 0.46f, 0.32f);
        }

        private static Material MatFor(int i, Material fallback)
        {
            if (_gunMats != null && i >= 0 && i < _gunMats.Length && _gunMats[i] != null)
                return _gunMats[i];
            return fallback;
        }

        private static void EnsureMeshes()
        {
            if (_meshesTried)
                return;
            _meshesTried = true;
            _meshes = new Mesh[GunCount];
            _gunTex = new Texture2D[GunCount];
            _gunMats = new Material[GunCount];
            string dir = ResolveMeshDir();
            Shader texSh = Shader.Find("Unlit/Texture");
            if (texSh == null)
                texSh = Shader.Find("Unlit/Color");
            if (texSh == null)
                texSh = Shader.Find("Standard");
            int n = 0;
            int nTex = 0;
            for (int i = 0; i < GunCount; i++)
            {
                _meshes[i] = TryLoad(dir, MeshFiles[i]);
                if (_meshes[i] != null)
                    n++;
                if (string.IsNullOrEmpty(dir))
                    continue;
                string png = Path.Combine(dir, Path.ChangeExtension(MeshFiles[i], ".png"));
                Texture2D tex = InfantryWeaponIcons.LoadPngFile(png);
                _gunTex[i] = tex;
                if (tex == null || texSh == null)
                    continue;
                nTex++;
                _gunMats[i] = new Material(texSh);
                _gunMats[i].mainTexture = tex;
                _gunMats[i].color = Color.white;
            }
            InfantryViewAnim.Ensure(dir);
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Infantry meshes " + n + "/" + GunCount + " tex " + nTex);
        }

        internal static void WarmStep()
        {
            if (_meshesTried)
                return;
            EnsureMeshes();
        }

        private static string ResolveMeshDir()
        {
            string[] names = new string[] { "GroundArsenal", "OritasyInfantry" };
            for (int i = 0; i < names.Length; i++)
            {
                string dir = null;
                try { dir = Path.Combine(Paths.PluginPath, names[i]); }
                catch { dir = null; }
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    return dir;
            }
            return null;
        }

        private static Mesh TryLoad(string dir, string file)
        {
            if (string.IsNullOrEmpty(dir))
                return null;
            string path = Path.Combine(dir, file);
            try { return InfantryObjLoader.Load(path); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Infantry OBJ " + file + ": " + ex.Message);
                return null;
            }
        }
    }
}
