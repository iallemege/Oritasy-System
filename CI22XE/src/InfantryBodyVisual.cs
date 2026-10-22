using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Clone the vanilla ejected-pilot mesh (PilotDismounted) onto infantry pawns.
    /// Visual only — Unit / network / colliders stay off.
    /// </summary>
    internal static class InfantryBodyVisual
    {
        private static GameObject _prefab;
        private static bool _tried;

        internal static void EnsureAttached(Transform parent)
        {
            if (parent == null)
                return;
            if (parent.Find("VanillaPilotBody") != null)
                return;
            Attach(parent);
        }

        internal static void Attach(Transform parent)
        {
            if (parent == null)
                return;
            if (parent.Find("VanillaPilotBody") != null)
                return;
            GameObject src = ResolvePrefab();
            if (src == null)
                return;
            bool srcOn = src.activeSelf;
            GameObject vis = null;
            try
            {
                if (srcOn)
                    src.SetActive(false);
                vis = UnityEngine.Object.Instantiate(src);
                if (srcOn)
                    src.SetActive(true);
            }
            catch
            {
                try
                {
                    if (srcOn && src != null)
                        src.SetActive(true);
                }
                catch { }
                return;
            }
            if (vis == null)
                return;
            vis.name = "VanillaPilotBody";
            vis.transform.SetParent(parent, false);
            vis.transform.localPosition = Vector3.zero;
            vis.transform.localRotation = Quaternion.identity;
            vis.transform.localScale = Vector3.one;
            vis.SetActive(false);
            StripGameplay(vis);
            HideChute(vis);
            vis.SetActive(true);
            InfantryBodyPoseDriver driver = vis.GetComponent<InfantryBodyPoseDriver>();
            if (driver == null)
                driver = vis.AddComponent<InfantryBodyPoseDriver>();
            driver.Speed = 0f;
            driver.Dead = false;
            DrivePose(driver, 0f, false);
        }

        internal static void Tick(Transform parent, float speed, bool dead)
        {
            if (parent == null)
                return;
            Transform body = parent.Find("VanillaPilotBody");
            if (body == null)
                return;
            InfantryBodyPoseDriver driver = body.GetComponent<InfantryBodyPoseDriver>();
            if (driver == null)
                driver = body.gameObject.AddComponent<InfantryBodyPoseDriver>();
            driver.Speed = speed;
            driver.Dead = dead;
            driver.EnsureAnim();
            DrivePose(driver, speed, dead);
        }

        internal static void SetLocalVisible(Transform parent, bool on)
        {
            if (parent == null)
                return;
            Transform body = parent.Find("VanillaPilotBody");
            if (body == null)
                return;
            if (body.gameObject.activeSelf != on)
                body.gameObject.SetActive(on);
        }

        internal static void PlayDead(Transform parent)
        {
            EnsureAttached(parent);
            SetLocalVisible(parent, true);
            Tick(parent, 0f, true);
        }

        private static void DrivePose(InfantryBodyPoseDriver driver, float speed, bool dead)
        {
            if (driver == null)
                return;
            driver.EnsureAnim();
            DrivePose(driver.Anim, speed, dead);
        }

        /// <summary>
        /// PilotController defaults to cockpit seated. Force land/stand (PilotState 4)
        /// or dead (5). Snap ejectLand to the last frame so they hold a standing pose.
        /// </summary>
        private static void DrivePose(Animator anim, float speed, bool dead)
        {
            if (anim == null)
                return;
            try
            {
                anim.SetBool("isSeated", false);
                anim.SetBool("isEjecting1", false);
                anim.SetBool("isDetaching", false);
                anim.SetBool("isParachuting", false);
                anim.SetBool("isLanded", !dead);
                anim.SetInteger("PilotState", dead ? 5 : 4);
                float run = 0f;
                if (!dead && speed > 0.35f)
                    run = Mathf.Clamp(speed / 4.2f, 0.55f, 1.8f);
                anim.SetFloat("RunSpeed", run > 0f ? run : 0.2f);
                if (anim.layerCount > 1)
                    anim.SetLayerWeight(1, run > 0f ? 1f : 0f);

                int want = Animator.StringToHash(dead ? "dead" : "ejectLand");
                if (!anim.HasState(0, want))
                    return;
                AnimatorStateInfo st = anim.GetCurrentAnimatorStateInfo(0);
                if (dead)
                {
                    if (st.shortNameHash != want)
                        anim.Play(want, 0, 0f);
                }
                else if (st.shortNameHash != want || st.normalizedTime < 0.97f)
                    anim.Play(want, 0, 0.99f);
            }
            catch { }
        }

        internal static void LateDrive(InfantryBodyPoseDriver driver)
        {
            if (driver == null || !driver.gameObject.activeInHierarchy)
                return;
            DrivePose(driver, driver.Speed, driver.Dead);
        }

        internal static void Warm()
        {
            if (_prefab != null || _tried)
                return;
            ResolvePrefab();
        }

        private static GameObject ResolvePrefab()
        {
            if (_prefab != null)
                return _prefab;
            if (_tried)
                return null;
            _tried = true;
            PilotDismounted[] all = null;
            try { all = Resources.FindObjectsOfTypeAll<PilotDismounted>(); }
            catch { return null; }
            if (all == null)
                return null;
            int bestScore = -1;
            GameObject best = null;
            for (int i = 0; i < all.Length; i++)
            {
                PilotDismounted pd = all[i];
                if (pd == null)
                    continue;
                GameObject go = pd.gameObject;
                if (go == null)
                    continue;
                string nm = go.name ?? "";
                if (nm.IndexOf("Oritasy", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (nm.IndexOf("VanillaPilotBody", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                Renderer[] rs = go.GetComponentsInChildren<Renderer>(true);
                int n = rs != null ? rs.Length : 0;
                if (n <= 0)
                    continue;
                bool prefab = false;
                try { prefab = !go.scene.IsValid() || !go.scene.isLoaded; }
                catch { }
                int score = n + (prefab ? 80 : 0);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = go;
                }
            }
            _prefab = best;
            return _prefab;
        }

        private static void StripGameplay(GameObject vis)
        {
            MonoBehaviour[] mbs = vis.GetComponentsInChildren<MonoBehaviour>(true);
            if (mbs != null)
            {
                for (int i = 0; i < mbs.Length; i++)
                {
                    MonoBehaviour mb = mbs[i];
                    if (mb == null || mb is InfantryBodyPoseDriver)
                        continue;
                    mb.enabled = false;
                }
            }
            Collider[] cols = vis.GetComponentsInChildren<Collider>(true);
            if (cols != null)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null)
                        cols[i].enabled = false;
                }
            }
            Rigidbody[] rbs = vis.GetComponentsInChildren<Rigidbody>(true);
            if (rbs != null)
            {
                for (int i = 0; i < rbs.Length; i++)
                {
                    if (rbs[i] == null)
                        continue;
                    rbs[i].isKinematic = true;
                    rbs[i].detectCollisions = false;
                }
            }
            Animator anim = vis.GetComponentInChildren<Animator>(true);
            if (anim != null)
            {
                anim.enabled = true;
                anim.cullingMode = AnimatorCullingMode.CullCompletely;
            }
        }

        private static void HideChute(GameObject vis)
        {
            Transform[] xs = vis.GetComponentsInChildren<Transform>(true);
            if (xs == null)
                return;
            for (int i = 0; i < xs.Length; i++)
            {
                Transform x = xs[i];
                if (x == null || x == vis.transform)
                    continue;
                string n = x.name;
                if (string.IsNullOrEmpty(n))
                    continue;
                string l = n.ToLowerInvariant();
                if (l.IndexOf("chute") >= 0 || l.IndexOf("parachute") >= 0
                    || l.IndexOf("ejectionseat") >= 0 || l.IndexOf("ejection_seat") >= 0
                    || l == "seat")
                    x.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Re-applies stand/dead after Animator Update, so the empty Any-State→seated
    /// transition cannot leave infantry sitting in the cockpit pose.
    /// </summary>
    internal sealed class InfantryBodyPoseDriver : MonoBehaviour
    {
        internal float Speed;
        internal bool Dead;
        internal Animator Anim;

        internal void EnsureAnim()
        {
            if (Anim != null)
                return;
            Anim = GetComponentInChildren<Animator>(true);
        }

        private void LateUpdate()
        {
            InfantryBodyVisual.LateDrive(this);
        }
    }
}
