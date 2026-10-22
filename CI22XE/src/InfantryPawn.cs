using UnityEngine;

namespace Oritasy
{
    /// <summary>Local super-soldier hitbox. Outer plate 10 (fatigues) over body armor 2.</summary>
    internal sealed class InfantryPawn : MonoBehaviour, IDamageable
    {
        private ArmorProperties _armor;

        private void Awake()
        {
            _armor = new ArmorProperties();
            _armor.pierceArmor = 40f;
            _armor.blastArmor = 12f;
            _armor.fireArmor = 8f;
            _armor.pierceTolerance = 10f;
            _armor.blastTolerance = 8f;
            _armor.fireTolerance = 6f;
            _armor.overpressureLimit = 25000f;
        }

        public void TakeDamage(float pierceDamage, float blastDamage, float amountAffected, float fireDamage, float ImpactDamage, PersistentID dealerID)
        {
            Vector3 incoming = Vector3.forward;
            try
            {
                Unit dealer = null;
                if (dealerID.IsValid && dealerID.TryGetUnit(out dealer) && dealer != null)
                    incoming = transform.position - dealer.transform.position;
            }
            catch { }
            InfantryModeService.ReceiveHit(pierceDamage, blastDamage, fireDamage, ImpactDamage, dealerID, incoming);
        }

        public void ApplyDamage(float pierceDamage, float blastDamage, float fireDamage, float impactDamage)
        {
            InfantryModeService.ReceiveHit(pierceDamage, blastDamage, fireDamage, impactDamage, PersistentID.None, Vector3.forward);
        }

        public void TakeShockwave(Vector3 origin, float overpressure, float blastPower)
        {
            Vector3 incoming = transform.position - origin;
            float pierce = 0f;
            float blast = blastPower * 0.02f;
            if (overpressure > 8000f)
                pierce = 20f;
            else if (overpressure > 3000f)
                pierce = 12f;
            InfantryModeService.ReceiveHit(pierce, blast, 0f, blastPower * 0.004f, PersistentID.None, incoming);
        }

        public void Detach(Vector3 velocity, Vector3 relativePos)
        {
        }

        public ArmorProperties GetArmorProperties()
        {
            return _armor;
        }

        public Unit GetUnit()
        {
            return InfantryModeService.LinkedUnit();
        }

        public float GetMass()
        {
            return 120f;
        }

        public Transform GetTransform()
        {
            return transform;
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(DamageEffects), "ArmorPenetrate")]
    internal static class Patch_ArmorPenetrate_Infantry
    {
        [HarmonyLib.HarmonyPostfix]
        private static void Postfix(Vector3 position, Vector3 velocity, float muzzleVelocity, float pierceDamage, float blastDamage, PersistentID dealerID)
        {
            InfantryModeService.TryAbsorbWorldHit(position, velocity, pierceDamage, blastDamage, dealerID);
        }
    }
}
