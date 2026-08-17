using HarmonyLib;

namespace Oritasy
{
    /// <summary>Vanilla kill-feed verbs → Chinese when Profile language is ZH.</summary>
    [HarmonyPatch(typeof(KillTypeExtensions), "GetVerb")]
    internal static class KillFeedVerbPatch
    {
        [HarmonyPostfix]
        private static void Postfix(KillType killType, bool hasKiller, ref string __result)
        {
            if (!UiLang.IsChinese || string.IsNullOrEmpty(__result))
                return;

            switch (__result)
            {
                case "shot down": __result = "击落"; break;
                case "destroyed": __result = "摧毁"; break;
                case "demolished": __result = "拆除"; break;
                case "intercepted": __result = "拦截"; break;
                case "sank": __result = "击沉"; break;
                case "crashed": __result = "坠毁"; break;
                case "was destroyed": __result = "被摧毁"; break;
                case "collapsed": __result = "坍塌"; break;
            }
        }
    }
}
