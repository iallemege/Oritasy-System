namespace Oritasy
{
    /// <summary>Same-assembly view over AirframeWearService for F8 / schematic.</summary>
    internal static class WearView
    {
        internal enum Family
        {
            JetAb = 0,
            JetDry = 1,
            Prop = 2,
            Helo = 3,
            Vtol = 4,
            Stovl = 5,
            Bomber = 6,
            Tilt = 7
        }

        internal struct Part
        {
            public string Id;
            public string En;
            public string Zh;
            public int Bank;
            public bool Secondary;
            public bool Prop;
            public float Health;
            public float Ceiling;
            public bool GSensitive;
            public bool AbSensitive;
            public float WearMul;
        }

        internal static int PartCount()
        {
            return AirframeWearService.PartCount();
        }

        internal static Part GetPart(int i)
        {
            return From(AirframeWearService.GetPart(i));
        }

        internal static int LoCount()
        {
            return AirframeWearService.LoCount();
        }

        internal static Family CurrentFamily()
        {
            return (Family)(int)AirframeWearService.CurrentFamily();
        }

        internal static int EngineCount()
        {
            return AirframeWearService.EngineCount();
        }

        internal static bool HasMixedAltitude()
        {
            return AirframeWearService.HasMixedAltitude();
        }

        internal static bool HasAfterburner()
        {
            return AirframeWearService.HasAfterburner();
        }

        internal static bool Flickering()
        {
            return AirframeWearService.Flickering();
        }

        internal static bool IsRepairing(int i)
        {
            return AirframeWearService.IsRepairing(i);
        }

        internal static bool IsQueued(int i)
        {
            return AirframeWearService.IsQueued(i);
        }

        internal static bool BankRunning(int bank)
        {
            return AirframeWearService.BankRunning(bank);
        }

        internal static void QueueRepair(int i)
        {
            AirframeWearService.QueueRepair(i);
        }

        internal static void RepairAllEngines(Aircraft ac)
        {
            AirframeWearService.RepairAllEngines(ac);
        }

        internal static float RepairProgress01()
        {
            return AirframeWearService.RepairProgress01();
        }

        internal static int RepairQueueCount()
        {
            return AirframeWearService.RepairQueueCount();
        }

        internal static float AbHoldSeconds()
        {
            return AirframeWearService.AbHoldSeconds();
        }

        internal static float AbTempC()
        {
            return AirframeWearService.AbTempC();
        }

        internal static float GHoldSeconds()
        {
            return AirframeWearService.GHoldSeconds();
        }

        internal static float PilotHealth01()
        {
            return AirframeWearService.PilotHealth01();
        }

        internal static float PartGGate()
        {
            return AirframeWearService.PartGGate();
        }

        internal static float RatedTempC()
        {
            return AirframeWearService.RatedTempC();
        }

        internal static float RatedThrottle01()
        {
            return AirframeWearService.RatedThrottle01();
        }

        internal static float CoreTempC()
        {
            return AirframeWearService.CoreTempC();
        }

        internal static float OilTempC()
        {
            return AirframeWearService.OilTempC();
        }

        internal static float OilTempRatio()
        {
            return AirframeWearService.OilTempRatio();
        }

        internal static string FamilyLabel()
        {
            return AirframeWearService.FamilyLabel();
        }

        internal static string MaterialLabel()
        {
            return AirframeWearService.MaterialLabel();
        }

        internal static string BankAltLabel(int bank)
        {
            return AirframeWearService.BankAltLabel(bank);
        }

        internal static string BankAltShort(int bank)
        {
            return AirframeWearService.BankAltShort(bank);
        }

        internal static bool IsApuPart(Part p)
        {
            return AirframeWearService.IsApuPart(To(p));
        }

        internal static bool PartIsRunning(Part p)
        {
            return AirframeWearService.PartIsRunning(To(p));
        }

        internal static int HealthPct(Part p)
        {
            return AirframeWearService.HealthPct(To(p));
        }

        internal static int CeilingPct(Part p)
        {
            return AirframeWearService.CeilingPct(To(p));
        }

        internal static string PartLabel(Part p)
        {
            return AirframeWearService.PartLabel(To(p));
        }

        internal static string PartTempHint(Part p)
        {
            return AirframeWearService.PartTempHint(To(p));
        }

        private static Part From(AirframeWearService.Part s)
        {
            Part p = new Part();
            p.Id = s.Id;
            p.En = s.En;
            p.Zh = s.Zh;
            p.Bank = s.Bank;
            p.Secondary = s.Secondary;
            p.Prop = s.Prop;
            p.Health = s.Health;
            p.Ceiling = s.Ceiling;
            p.GSensitive = s.GSensitive;
            p.AbSensitive = s.AbSensitive;
            p.WearMul = s.WearMul;
            return p;
        }

        private static AirframeWearService.Part To(Part p)
        {
            AirframeWearService.Part s = new AirframeWearService.Part();
            s.Id = p.Id;
            s.En = p.En;
            s.Zh = p.Zh;
            s.Bank = p.Bank;
            s.Secondary = p.Secondary;
            s.Prop = p.Prop;
            s.Health = p.Health;
            s.Ceiling = p.Ceiling;
            s.GSensitive = p.GSensitive;
            s.AbSensitive = p.AbSensitive;
            s.WearMul = p.WearMul;
            return s;
        }
    }
}
