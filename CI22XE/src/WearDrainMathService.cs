namespace Oritasy
{
    internal static class WearDrainMathService
    {
        internal static float PilotDrainDelta(float g, float dt, float gGate)
        {
            if (g <= gGate || dt <= 0f)
                return 0f;
            float over = g - gGate;
            float rate = 0.004f * over;
            if (g > gGate + 3f)
                rate *= 2.2f;
            return rate * dt;
        }

        internal static void PartDrainRates(
            float g, float partGGate, float gHold, bool afterburner,
            float abHold, float abWearAfter, out float gRate, out float abRate)
        {
            gRate = 0f;
            abRate = 0f;
            if (g > partGGate && gHold > 2f)
                gRate = 0.00018f * (g - partGGate);
            if (afterburner && abHold > abWearAfter)
            {
                float over = abHold - abWearAfter;
                if (over > 300f) over = 300f;
                abRate = 0.00022f + 0.000012f * over;
            }
        }

        internal static float ApplyHealthDelta(float health, float ceiling, float delta)
        {
            float h = health - delta;
            if (h < 0.02f) h = 0.02f;
            if (h > ceiling) h = ceiling;
            return h;
        }
    }
}