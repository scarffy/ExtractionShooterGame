namespace ExtractionShooter.Gameplay.Weapons
{
    /// <summary>
    /// Cartridge families. A weapon loads only ammo of its own caliber.
    /// Values are serialized in assets: append new calibers, never reorder.
    /// </summary>
    public enum Caliber
    {
        Mm9x19 = 0,
        Acp45 = 1,
        Ae50 = 2,
        Mm46x30 = 3,
        Mm556x45 = 4,
        Mm545x39 = 5,
        Mm762x39 = 6,
        Mm762x51 = 7,
        Mm762x54R = 8,
        Gauge12 = 9,
        Lm338 = 10,
    }

    public static class CaliberNames
    {
        public static string Display(Caliber caliber)
        {
            switch (caliber)
            {
                case Caliber.Mm9x19: return "9x19mm";
                case Caliber.Acp45: return ".45 ACP";
                case Caliber.Ae50: return ".50 AE";
                case Caliber.Mm46x30: return "4.6x30mm";
                case Caliber.Mm556x45: return "5.56x45mm";
                case Caliber.Mm545x39: return "5.45x39mm";
                case Caliber.Mm762x39: return "7.62x39mm";
                case Caliber.Mm762x51: return "7.62x51mm";
                case Caliber.Mm762x54R: return "7.62x54mmR";
                case Caliber.Gauge12: return "12 gauge";
                case Caliber.Lm338: return ".338 Lapua Magnum";
                default: return caliber.ToString();
            }
        }
    }
}
